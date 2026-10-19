using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Sonar.Models;
using System.Net.Sockets;
using System.Threading;
using Sonar.Utilities;
using MessagePack;
using System.Net.WebSockets;
using Sonar.Messages;
using Sonar.Enums;
using System.Threading.Tasks.Dataflow;
using Sonar.Config;
using Sonar.Sockets;
using Sonar.Trackers;
using static Sonar.Utilities.UnixTimeHelper;
using Sonar.Services;
using Sonar.Logging;
using System.Net;
using System.Net.Http;
using static Sonar.SonarConstants;
using Sonar.Data;
using Sonar.Data.Details;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Container = DryIoc.Container;
using DryIoc;
using Sonar.Connections;
using System.Reflection;
using System.Linq;
using DryIoc.MefAttributedModel;
using Sonar.Tokens;
using Sonar.Extensions;
using SonarUtils.Secrets;

namespace Sonar
{
    /// <summary>Main entry point of Sonar</summary>
    public sealed partial class SonarClient : IDisposable, IAsyncDisposable
    {
        private readonly Container _container;
        private readonly PingService _pinger;
        internal readonly SonarTickService _ticker;
        internal ClientModifiers Modifiers = ClientModifiers.Defaults;

        private ClientIdentifier ClientIdentifier { get; set; }

        // ClientHello never carries a hardware identifier. A server that refuses such clients can only be
        // seen as sessions dropping right after the hello, so warn once per load after a few in a row.
        private const long ShortSessionMs = 15_000;
        private const int ShortSessionWarnCount = 3;
        private long _helloSentTicks; // Interlocked; 0 = no ClientHello on the active connection
        private int _shortSessions; // Interlocked
        private int _shortSessionWarned; // Interlocked

        private SonarVersion versionInfo = new();
        /// <summary>
        /// Version information
        /// </summary>
        public SonarVersion VersionInfo
        {
            get => Unsafe.As<SonarVersion>(this.versionInfo.Clone());
            set
            {
                this.versionInfo = Unsafe.As<SonarVersion>(value.Clone());
                this.versionInfo.ResetSonarVersion();
                this.Connection.SendIfConnected(value);
            }
        }

        public SonarMeta Meta { get; }

        /// <summary>Sonar Configuration</summary>
        public SonarConfig Configuration { get; }

        /// <summary>Sonar relay trackers</summary>
        public RelayTrackers Trackers { get; }

        /// <summary>Sonar connection information</summary>
        public SonarConnectionManager Connection { get; }

        /// <summary>Current ping in milliseconds</summary>
        public double Ping => this._pinger.Ping;

        /// <summary>Sonar Start Info</summary>
        public SonarStartInfo StartInfo { get; }

        /// <summary>Sonar ID</summary>
        public string? ClientHash => this.ClientIdentifier.ClientHash;

        /// <summary>Constructs a <see cref="SonarClient"/>. Only one can exist at a time. Remember to call <see cref="Start"/>.</summary>
        public SonarClient(SonarStartInfo? startInfo = null)
        {
            ClientCreated();

            this._container = new(Rules.Default.With(FactoryMethod.Constructor(false, true)));
            this.PrepareDryIoc();

            this.StartInfo = startInfo ?? new();
            this.StartInfo.Initialize();

            // Ahead of the container: SonarConnectionManager logs from its own constructor and
            // hands this logger to SonarUrlManager, whose background task outlives construction.
            this.Logger = new SonarLoggerContext(this.baseLogger, "Sonar");

            this.Connection = this._container.Resolve<SonarConnectionManager>();
            this.Connection.MessageReceived += this.ReceiveHandler;
            this.Connection.TextReceived += this.TextHandler;
            this.Connection.ConnectedInternal += this.ReadyHandler;
            this.Connection.DisconnectedInternal += this.DisconnectedHandler;

            this.ClientIdentifier = IdentifierUtils.GetClientIdentifier(this.StartInfo);

            this.baseLogger.LogMessage += this.LogHandler;
            this.serverLogger = new SonarLoggerContext(this.baseLogger, "Server");

            this._ticker = new(this);
            this._pinger = new(this);
            this.Configuration = new();
            this.Configuration.BindClient(this);
            this.Meta = this._container.Resolve<SonarMeta>();
            this.Trackers = this._container.Resolve<RelayTrackers>();

            this._ticker.Tick += this.Ticker_Tick;
            this._pinger.Pong += this.Pinger_Pong;

            this.baseLogger.Level = this.Configuration.LogLevel;
        }

        private void PrepareDryIoc()
        {
            var assembly = Assembly.GetExecutingAssembly();
            this._container.RegisterInstance(this, setup: Setup.With(preventDisposal: true)); // SonarClient is disposed by the user
            this._container.RegisterInstance(this._container, setup: Setup.With(preventDisposal: true)); // SonarClient is disposed by the user
            this._container.RegisterExports(assembly);

            // Relay trackers
            this._container.RegisterMany(Made.Of(r => ServiceInfo.Of<RelayTrackers>(), t => t.Hunts), Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            this._container.RegisterMany(Made.Of(r => ServiceInfo.Of<RelayTrackers>(), t => t.Fates), Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            this._container.RegisterMany(Made.Of(r => ServiceInfo.Of<RelayTrackers>(), t => t.Utils), Reuse.Singleton, setup: Setup.With(preventDisposal: true));
        }

        /// <summary>Starts connecting to the Sonar server. Calling this method while started has no effect and returns <see cref="false"/></summary>
        /// <remarks>Until this is called nothing is sent anywhere; trackers work locally from the embedded database.</remarks>
        public bool Start()
        {
            return this.Connection.Start();
        }

        /// <summary>Disconnects from the Sonar server and stops reconnecting until <see cref="Start"/> is called again.</summary>
        public bool Stop()
        {
            return this.Connection.Stop();
        }

        /// <summary>Request all relay data from the Sonar Server</summary>
        public void RequestRelayData()
        {
            this.Connection.SendIfConnected(() => new RelayDataRequest());
        }

        private void ReadyHandler(SonarConnectionManager arg1, ISonarSocket arg2, uint arg3)
        {
            this._pinger.Poke();
            var taken = false;
            this.Meta._lock.Enter(ref taken);
            try
            {
                var messages = new MessageList()
                {
                    new ClientHello()
                    {
                        ClientId = this.ClientIdentifier.ClientId,
                        ClientSecret = this.ClientIdentifier.ClientSecret,

                        HardwareIdentifier = null, // Deliberately not sent: no machine UUID leaves this machine
                        Version = this.VersionInfo,
                        SecretMetaBytes = new Dictionary<string, ImmutableArray<byte>?>()
                        {
                            { "plugin", this.StartInfo.PluginSecretMeta },
                            { "sonar", SecretUtils.GetSecretMetaBytes(typeof(SonarClient).Assembly) },
                            { "utils", SecretUtils.GetSecretMetaBytes(typeof(SecretUtils).Assembly) },
                        }
                    },

                    this.Configuration.Contribute,
                    this.Meta.PlayerInfo!,
                    this.Meta.PlayerPosition!,
                    Database.GetDbInfo(),
                };
                this.Connection.SendIfConnected(messages);
                Interlocked.Exchange(ref this._helloSentTicks, Math.Max(1, System.Environment.TickCount64));
            }
            finally
            {
                this.Meta._lock.Exit();
            }
        }

        private void DisconnectedHandler(SonarConnectionManager manager, ISonarSocket socket)
        {
            var helloTicks = Interlocked.Exchange(ref this._helloSentTicks, 0);
            if (helloTicks == 0 || !manager.IsStarted) return; // Stop() is the user's choice, not a refusal
            var lastedMs = System.Environment.TickCount64 - helloTicks;
            if (lastedMs >= ShortSessionMs)
            {
                Interlocked.Exchange(ref this._shortSessions, 0);
                return;
            }
            var count = Interlocked.Increment(ref this._shortSessions);
            if (count >= ShortSessionWarnCount && Interlocked.Exchange(ref this._shortSessionWarned, 1) == 0)
            {
                this.LogWarning($"Sonar 連線連續 {count} 次在送出識別訊息後 {ShortSessionMs / 1000} 秒內就斷開（本插件不送硬體識別碼與作業系統版本）：可能是伺服器拒絕不帶硬體識別碼或作業系統版本的連線，其他玩家回報的狩獵怪與 F.A.T.E. 會收不到；本次載入只提示一次");
            }
        }

        private void ReceiveHandler(SonarConnectionManager arg1, ISonarMessage message)
        {
            switch (message)
            {
                case ClientModifiers modifiers:
                    //if (this.LogVerboseEnabled) this.LogVerbose($"Client Modifiers received: {modifiers}");
                    this.Modifiers.MutateWith(modifiers);
                    break;

                case SonarHeartbeat heartbeat:
                    UnixTimeOffset = (heartbeat.UnixTime - UnixNow) - this.Ping / 2;
                    if (this.LogVerboseEnabled)
                    {
                        var syncMessage = UnixTimeOffset is 0 ? "perfectly synchronized with the server" :
                            $"{Math.Abs(UnixTimeOffset)}ms {(UnixTimeOffset > 0 ? "behind" : "ahead")}";

                        var placeCounts = heartbeat.ClientPlaceCounts;
                        var placeWorldIndex = this.Meta.PlayerPosition?.GetIndexKey(Indexes.IndexType.World) ?? string.Empty;
                        var placeDatacenterIndex = this.Meta.PlayerPosition?.GetIndexKey(Indexes.IndexType.Datacenter) ?? string.Empty;
                        var placeRegionIndex = this.Meta.PlayerPosition?.GetIndexKey(Indexes.IndexType.Region) ?? string.Empty;
                        var placeAudienceIndex = this.Meta.PlayerPosition?.GetIndexKey(Indexes.IndexType.Audience) ?? string.Empty;
                        var placeCountsText = string.Join(" / ",
                            $"{placeCounts.GetValueOrDefault(placeWorldIndex)}",
                            $"{placeCounts.GetValueOrDefault(placeDatacenterIndex)}",
                            $"{placeCounts.GetValueOrDefault(placeRegionIndex)}",
                            $"{placeCounts.GetValueOrDefault(placeAudienceIndex)}",
                            $"{placeCounts.GetValueOrDefault("all")}"
                        );

                        var homeCounts = heartbeat.ClientHomeCounts;
                        var homeWorldIndex = this.Meta.PlayerInfo?.GetIndexKey(Indexes.IndexType.World) ?? string.Empty;
                        var homeDatacenterIndex = this.Meta.PlayerInfo?.GetIndexKey(Indexes.IndexType.Datacenter) ?? string.Empty;
                        var homeRegionIndex = this.Meta.PlayerInfo?.GetIndexKey(Indexes.IndexType.Region) ?? string.Empty;
                        var homeAudienceIndex = this.Meta.PlayerInfo?.GetIndexKey(Indexes.IndexType.Audience) ?? string.Empty;
                        var homeCountsText = string.Join(" / ",
                            $"{homeCounts.GetValueOrDefault(homeWorldIndex)}",
                            $"{homeCounts.GetValueOrDefault(homeDatacenterIndex)}",
                            $"{homeCounts.GetValueOrDefault(homeRegionIndex)}",
                            $"{homeCounts.GetValueOrDefault(homeAudienceIndex)}",
                            $"{homeCounts.GetValueOrDefault("all")}"
                        );

                        this.LogVerbose($"Sonar Heartbeat Received (Player Counts: [Place: {placeCountsText} | Home: {homeCountsText}] | Time is {syncMessage})");
                    }
                    break;

                case SonarPing ping:
                    //if (this.LogDebugEnabled) this.LogDebug($"Ping request received from Server {ping.Sequence}");
                    this.Connection.SendIfConnected(new SonarPong() { Sequence = ping.Sequence });
                    break;

                case SonarLogMessage log:
                    this.serverLogger.Log(log.Message, log.Level);
                    break;

                case SonarMessage msg:
                    if (this.LogErrorEnabled) this.LogError($"Sonar Server Message received: {msg.Message}");
                    try
                    {
                        this.ServerMessage?.Invoke(this, msg.Message);
                    }
                    catch (Exception ex)
                    {
                        if (this.LogErrorEnabled) this.LogError(ex, string.Empty);
                    }
                    break;

                case ClientIdentifier identifier:
                    IdentifierUtils.SaveClientIdentifier(identifier, this.StartInfo);
                    this.ClientIdentifier = identifier;
                    break;

                case SupportResponse response:
                    this.SupportCallback(response);
                    break;

                case SonarDb db:
                    this.LogInformation("Received Sonar Database update from server");
                    this.LogDebug(db.ToString());
                    Database.Instance = db;
                    break;
            }
        }

        private void TextHandler(SonarConnectionManager arg1, string text)
        {
            // NOTE: This should never happen
            if (this.LogInformationEnabled) this.LogInformation($"[Server text] {text}");
        }

        private void Ticker_Tick(SonarTickService ticker)
        {
            try
            {
                this.Tick?.Invoke(this);
            }
            catch (Exception ex)
            {
                if (this.LogErrorEnabled) this.LogError(ex, string.Empty);
            }
        }
        private void Pinger_Pong(PingService pinger, double ping)
        {
            try
            {
                this.Pong?.Invoke(this, ping);
            }
            catch (Exception ex)
            {
                if (this.LogErrorEnabled) this.LogError(ex, string.Empty);
            }
        }

        public event SonarClientEventHandler? Tick;
        public event SonarClientPingHandler? Pong;

        public event SonarServerMessageHandler? ServerMessage;
        public event Action<SonarClient, SonarToken>? TokenReceived;

#region Disposable Interface
        private int disposed; // Interlocked
        public bool IsDisposed => this.disposed == 1;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 1) return;

            this._ticker.Tick -= this.Ticker_Tick;
            this._pinger.Pong -= this.Pinger_Pong;

            await this._ticker.DisposeAsync().ConfigureAwait(false);
            await this._pinger.DisposeAsync().ConfigureAwait(false);

            this.baseLogger.LogMessage -= this.LogHandler;
            this._container.Dispose();

            GC.SuppressFinalize(this);
        }
        // Dalamud 會在主執行緒上呼叫這個 Dispose,所以等待必須有界(與底下 PingService、SonarSocketSignalR 的
        // 5 秒上限一致):無上限等待時任何一層卡住都會讓整個遊戲在卸載插件時凍結。
        [SuppressMessage("Usage", "CA1816", Justification = "Called in DisposeAsync")]
        public void Dispose() => this.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        ~SonarClient()
        {
            ClientFinalized();
            try
            {
                this.Dispose();
            }
            catch
            {
                /* Swallowed or else .NET will crash */
            }
        }
#endregion
    }
}
