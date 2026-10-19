using Dalamud.Plugin.Services;
using DryIocAttributes;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Microsoft.Extensions.Hosting;
using Sonar;
using Sonar.Data;
using Sonar.Enums;
using Sonar.Indexes;
using Sonar.Relays;
using Sonar.Trackers;
using Sonar.Utilities;
using SonarPlugin.Game;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Sonar.SonarConstants;

namespace SonarPlugin.Trackers
{
    [ExportMany]
    [SingletonReuse]
    public sealed class SonarFateProvider : IHostedService
    {
        private PlayerCounterService Players { get; }
        private SonarFramework Framework { get; }
        private SonarMeta Meta { get; }
        private IRelayTracker<FateRelay> Tracker { get; }
        private IPluginLog Logger { get; }

        /// <summary>
        /// Initialize fate tracker
        /// </summary>
        /// <param name="plugin">Sonar Plugin object</param>
        /// <param name="debug">(Optional) Output debug logging</param>
        public SonarFateProvider(PlayerCounterService players, SonarFramework framework, SonarMeta meta, IRelayTracker<FateRelay> tracker, IPluginLog logger)
        {
            // Get Sonar and Plugin Interface
            this.Players = players;
            this.Framework = framework;
            this.Meta = meta;
            this.Tracker = tracker;
            this.Logger = logger;

            // Initialization feedback
            this.Logger.Information("FateTracker Initialized");
        }

        private List<uint> _nextFateIds = [];
        private List<uint> _lastFateIds = [];
        private readonly HashSet<uint> _seenFateIds = [];

        // One relay per fate, refilled every frame. FeedTransientRelay copies whatever the tracker keeps,
        // so these are never stored or sent; framework thread only.
        private readonly Dictionary<uint, FateRelay> _relays = [];

        private FateRelay GetRelay(uint id, uint worldId, uint zoneId, uint instanceId)
        {
            if (!this._relays.TryGetValue(id, out var relay) || !relay.WorldZoneInstanceEquals(worldId, zoneId, instanceId))
            {
                this._relays[id] = relay = new FateRelay() { Id = id, WorldId = worldId, ZoneId = zoneId, InstanceId = instanceId };
            }
            return relay;
        }

        private unsafe void FrameworkTick(SonarFramework framework)
        {
            // Don't proceed if the structures aren't ready
            if (!framework.IsSafe()) goto Fail;

            // Fate Manager
            var manager = FateManager.Instance();
            if (manager is null) goto Fail;

            // Get player position information
            var playerPosition = this.Meta.PlayerPosition;
            if (playerPosition is null) goto Fail;

            // Place and check timestamp
            var worldId = playerPosition.WorldId;
            var zoneId = playerPosition.ZoneId;
            var instanceId = playerPosition.InstanceId;
            double? timestamp = null;

            var fates = manager->Fates.AsSpan();
            var currentFateIds = this._nextFateIds;
            foreach (var fatePtr in fates)
            {
                var fate = fatePtr.Value;
                if (fate is null) continue; // && fate->State is not 0 && (fate->State is FateState.Preparing || (fate->StartTimeEpoch is not 0 && fate->Duration is not 0)))

                var state = fate->State;
                if (state is 0) continue;

                var id = fate->FateId;
                if (!Database.Fates.ContainsKey(id)) continue;

                var startTimeEpoch = fate->StartTimeEpoch;
                if (startTimeEpoch is 0 && state is not FateState.Preparing) continue;
                if (startTimeEpoch is not 0 && state is FateState.Preparing) continue;

                var duration = fate->Duration;
                var position = fate->Location;
                if (position.X is 0 && position.Y is 0 && position.Z is 0) continue;

                currentFateIds.Add(id);
                var relay = this.GetRelay(id, worldId, zoneId, instanceId);
                relay.Coords = position.SwapYZ();
                relay.StartTime = startTimeEpoch * EarthSecond;
                relay.Duration = duration * EarthSecond;
                relay.Progress = fate->Progress;
                relay.Status = state.ToSonarFateStatus();
                relay.Players = this.Players.GetCount(position);
                relay.CheckTimestamp = timestamp ??= UnixTimeHelper.SyncedUnixNow;
                relay.Bonus = fate->IsBonus;
                this.Tracker.FeedTransientRelay(relay);
            }


            // Determine and mark disappeared fates as failed
            var lastFateIds = this._lastFateIds;
            var fateStates = this.Tracker.Data.States;
            // Same algorithm as lastFateIds.Except(currentFateIds), without the per-tick iterator and set.
            var seenFateIds = this._seenFateIds;
            seenFateIds.Clear();
            if (lastFateIds.Count != 0)
            {
                foreach (var id in currentFateIds) seenFateIds.Add(id);
            }
            foreach (var fateId in lastFateIds)
            {
                if (!seenFateIds.Add(fateId)) continue;
                this._relays.Remove(fateId);
                var fateKey = IndexUtils.GetWorldZoneInstanceIndexKey(worldId, fateId, instanceId); // NOTE: Key formats are the same
                var fateState = fateStates.GetValueOrDefault(fateKey);
                if (fateState is null) continue;
                var fate = fateState.Relay.Clone();
                fate.Status = FateStatus.Failed;
                this.Tracker.FeedRelay(fate);
            }
            this._lastFateIds = currentFateIds;

            // Clear last fate keys and place the list into pooled list for next iteration
            lastFateIds.Clear();
            this._nextFateIds = lastFateIds;
            return;

        Fail:
            this._lastFateIds.Clear();
            this._relays.Clear();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.Framework.Update += this.FrameworkTick;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.Framework.Update -= this.FrameworkTick;
            return Task.CompletedTask;
        }
    }
}
