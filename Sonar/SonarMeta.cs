using DryIocAttributes;
using Sonar.Models;
using System;
using System.Threading;
using static Sonar.SonarConstants;
using Sonar.Extensions;
using System.Collections.Immutable;
using Sonar.Numerics;

namespace Sonar
{
    [ExportEx]
    [SingletonReuse]
    public sealed class SonarMeta
    {
        internal SpinLock _lock = new(false);
        private ImmutableArray<Action<PlayerInfo>> _infoHandlers = [];
        private ImmutableArray<Action<PlayerPosition>> _placeHandlers = [];
        private ImmutableArray<Action<PlayerPosition>> _positionHandlers = [];

        private SonarClient Client { get; }

        public PlayerInfo? PlayerInfo { get; private set; }
        public PlayerPosition? PlayerPosition { get; private set; }

        internal SonarMeta(SonarClient client)
        {
            this.Client = client;
            this.Client.Connection.MessageReceived += this.MessageHandler;
        }

        private void MessageHandler(Connections.SonarConnectionManager _, Messages.ISonarMessage message)
        {
            switch (message)
            {
                case LodestoneVerificationNeeded need:
                    this.VerificationNeeded?.SafeInvoke(this, need);
                    break;

                case LodestoneVerificationResult result:
                    this.VerificationResult?.SafeInvoke(this, result);
                    break;
            }
        }

        /// <summary>Update player information</summary>
        /// <param name="playerInfo">Player information</param>
        /// <returns>Succeeded</returns>
        /// <remarks>
        /// This method will always succeed unless one of the following happens:
        /// <list type="bullet">
        /// <item><see cref="Models.PlayerInfo"/> is the same (unchanged)</item>
        /// <item><see cref="Models.PlayerInfo"/> is <c>null</c></item>
        /// </list>
        /// </remarks>
        public bool UpdatePlayerInfo(PlayerInfo? playerInfo)
        {
            if (playerInfo is null || playerInfo.Equals(this.PlayerInfo)) return false;

            this.PlayerInfo = playerInfo;
            this.Client.Connection.SendIfConnected(playerInfo);

            foreach (var handler in this._infoHandlers.AsSpan())
            {
                try
                {
                    handler(playerInfo);
                }
                catch (Exception ex)
                {
                    this.Client.LogError(ex, "Exception occured at PlayerInfoChanged handler");
                }
            }
            return true;
        }

        /// <summary>Update player position</summary>
        /// <param name="playerPosition">Player position</param>
        /// <returns>Succeeded</returns>
        /// <remarks>
        /// This method will always succeed unless one of the following happens:
        /// <list type="bullet">
        /// <item><see cref="Models.PlayerPosition"/> is the same (unchanged)</item>
        /// <item><see cref="Models.PlayerPosition"/> is <c>null</c></item>
        /// </list>
        /// </remarks>
        public (bool PlaceUpdated, bool PositionUpdated) UpdatePlayerPosition(PlayerPosition? playerPosition)
        {
            if (playerPosition is null) return (false, false);
            var (placeUpdated, positionUpdated) = this.GetPlayerPositionChange(playerPosition.WorldId, playerPosition.ZoneId, playerPosition.InstanceId, playerPosition.Coords);
            if (!placeUpdated && !positionUpdated) return (false, false);

            this.CommitPlayerPosition(playerPosition, placeUpdated, positionUpdated);
            return (placeUpdated, positionUpdated);
        }

        /// <summary>Same as <see cref="UpdatePlayerPosition(Models.PlayerPosition?)"/>, but only allocates the <see cref="Models.PlayerPosition"/> when it is going to be stored.</summary>
        /// <param name="playerPosition">The stored position when anything was updated; otherwise <see langword="null"/>.</param>
        public (bool PlaceUpdated, bool PositionUpdated) UpdatePlayerPosition(uint worldId, uint zoneId, uint instanceId, SonarVector3 coords, out PlayerPosition? playerPosition)
        {
            var (placeUpdated, positionUpdated) = this.GetPlayerPositionChange(worldId, zoneId, instanceId, coords);
            if (!placeUpdated && !positionUpdated)
            {
                playerPosition = null;
                return (false, false);
            }

            playerPosition = new PlayerPosition() { WorldId = worldId, ZoneId = zoneId, InstanceId = instanceId, Coords = coords };
            this.CommitPlayerPosition(playerPosition, placeUpdated, positionUpdated);
            return (placeUpdated, positionUpdated);
        }

        private (bool PlaceUpdated, bool PositionUpdated) GetPlayerPositionChange(uint worldId, uint zoneId, uint instanceId, SonarVector3 coords)
        {
            var current = this.PlayerPosition;
            var placeUpdated = current is null || !current.WorldZoneInstanceEquals(worldId, zoneId, instanceId);
            var positionUpdated = placeUpdated || coords.Delta(current!.Coords).LengthSquared() >= RoughDistanceSquared;
            return (placeUpdated, positionUpdated);
        }

        private void CommitPlayerPosition(PlayerPosition playerPosition, bool placeUpdated, bool positionUpdated)
        {
            this.PlayerPosition = playerPosition;
            this.Client.Connection.SendIfConnected(playerPosition);

            if (positionUpdated)
            {
                foreach (var handler in this._positionHandlers.AsSpan())
                {
                    try
                    {
                        handler(playerPosition);
                    }
                    catch (Exception ex)
                    {
                        this.Client.LogError(ex, "Exception occured at PlayerPositionChanged handler");
                    }
                }
            }

            if (placeUpdated)
            {
                foreach (var handler in this._placeHandlers.AsSpan())
                {
                    try
                    {
                        handler(playerPosition);
                    }
                    catch (Exception ex)
                    {
                        this.Client.LogError(ex, "Exception occured at PlayerPlaceChanged handler");
                    }
                }
            }
        }

        public void RequestVerification()
        {
            this.Client.Connection.SendIfConnected(new LodestoneVerificationRequest());
        }

        public event Action<PlayerInfo>? InfoChanged
        {
            add
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._infoHandlers, (handlers, handler) => handlers.Add(handler), value);
            }
            remove
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._infoHandlers, (handlers, handler) => handlers.Remove(handler), value);
            }
        }

        public event Action<PlayerPosition>? PlaceChanged
        {
            add
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._placeHandlers, (handlers, handler) => handlers.Add(handler), value);
            }
            remove
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._placeHandlers, (handlers, handler) => handlers.Remove(handler), value);
            }
        }

        public event Action<PlayerPosition>? PositionChanged
        {
            add
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._positionHandlers, (handlers, handler) => handlers.Add(handler), value);
            }
            remove
            {
                if (value is not null) ImmutableInterlocked.Update(ref this._positionHandlers, (handlers, handler) => handlers.Remove(handler), value);
            }
        }

        public event Action<SonarMeta, LodestoneVerificationNeeded>? VerificationNeeded;
        public event Action<SonarMeta, LodestoneVerificationResult>? VerificationResult;
    }
}
