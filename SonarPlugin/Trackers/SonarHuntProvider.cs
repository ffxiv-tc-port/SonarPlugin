using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using DryIocAttributes;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Microsoft.Extensions.Hosting;
using Sonar;
using Sonar.Data;
using Sonar.Models;
using Sonar.Relays;
using Sonar.Trackers;
using Sonar.Utilities;
using SonarPlugin.Game;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CSVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace SonarPlugin.Trackers
{
    [ExportMany]
    [SingletonReuse]
    public sealed class SonarHuntProvider : IHostedService
    {
        private PlayerCounterService Players { get; }
        private IRelayTracker<HuntRelay> Tracker { get; }
        private SonarFramework Framework { get; }
        private SonarMeta Meta { get; }
        private IPluginLog Logger { get; }

        /// <summary>
        /// Initialize monster tracker
        /// </summary>
        public SonarHuntProvider(PlayerCounterService players, IRelayTracker<HuntRelay> tracker, SonarFramework framework, SonarMeta meta, IPluginLog logger)
        {
            // Get Sonar and Plugin Interface
            this.Players = players;
            this.Tracker = tracker;
            this.Framework = framework;
            this.Meta = meta;
            this.Logger = logger;

            // Initialization feedback
            this.Logger.Information("MobTracker Initialized");
        }

        // One relay per hunt actor, refilled every frame. FeedTransientRelay copies whatever the tracker keeps,
        // so these are never stored or sent; framework thread only.
        private readonly Dictionary<uint, HuntRelay> _relays = [];
        private readonly HashSet<uint> _seenActorIds = [];
        private readonly List<uint> _staleActorIds = [];

        // Keyed by actor: an SS minion's cached RelayKey includes ActorId, so a relay never moves to another actor.
        private HuntRelay GetRelay(uint actorId, uint id, uint worldId, uint zoneId, uint instanceId)
        {
            if (!this._relays.TryGetValue(actorId, out var relay) || relay.Id != id || !relay.WorldZoneInstanceEquals(worldId, zoneId, instanceId))
            {
                this._relays[actorId] = relay = new HuntRelay() { Id = id, ActorId = actorId, WorldId = worldId, ZoneId = zoneId, InstanceId = instanceId };
            }
            return relay;
        }

        private unsafe void FrameworkTick(SonarFramework framework)
        {
            // Don't proceed if the structures aren't ready
            if (!framework.IsSafe()) return;

            // Character Manager
            var manager = CharacterManager.Instance();
            if (manager is null) return;

            // Get player position information
            var playerPosition = this.Meta.PlayerPosition;
            if (playerPosition is null) return;

            // Place and check timestamp
            var worldId = playerPosition.WorldId;
            var zoneId = playerPosition.ZoneId;
            var instanceId = playerPosition.InstanceId;
            double? timestamp = null;

            var seenActorIds = this._seenActorIds;
            seenActorIds.Clear();
            var characters = manager->BattleCharas;
            foreach (var characterPtr in characters)
            {
                var character = characterPtr.Value;
                if (character is null || character->ObjectKind is not ObjectKind.BattleNpc) continue;

                var id = character->NameId;
                if (!Database.Hunts.ContainsKey(id)) continue;

                var position = Unsafe.As<CSVector3, Vector3>(ref character->Position);

                var actorId = character->EntityId;
                seenActorIds.Add(actorId);
                var relay = this.GetRelay(actorId, id, worldId, zoneId, instanceId);
                relay.Coords = position.SwapYZ();
                relay.CurrentHp = character->Health;
                relay.MaxHp = character->MaxHealth;
                relay.Players = this.Players.GetCount(position);
                relay.CheckTimestamp = timestamp ??= UnixTimeHelper.SyncedUnixNow;
                this.Tracker.FeedTransientRelay(relay);
            }

            // Every seen actor has an entry, so equal counts mean nothing went away.
            if (this._relays.Count != seenActorIds.Count)
            {
                var stale = this._staleActorIds;
                foreach (var actorId in this._relays.Keys)
                {
                    if (!seenActorIds.Contains(actorId)) stale.Add(actorId);
                }
                foreach (var actorId in stale) this._relays.Remove(actorId);
                stale.Clear();
            }
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
