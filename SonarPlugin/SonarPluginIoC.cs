using Sonar;
using System;
using DryIoc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Plugin;
using Dalamud.IoC;
using Dalamud.Game;
using SonarPlugin.Utility;
using Sonar.Data;
using Sonar.Enums;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;
using System.ComponentModel;
using Container = DryIoc.Container;
using IContainer = DryIoc.IContainer;
using Sonar.Trackers;
using Dalamud.Plugin.Services;
using SonarUtils.Text.Placeholders;
using SonarUtils.Secrets;
using Microsoft.Extensions.DependencyInjection;
using DryIoc.MefAttributedModel;
using SonarUtils;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using SonarPlugin.Logging;
using SonarPlugin.Events;

namespace SonarPlugin
{
    public sealed class SonarPluginIoC : IDisposable
    {
        private readonly Container _container;
        private FileDialogManager? _fileDialogs;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public IContainer Container => this._container; // Only stub should access this

        private SonarPluginStub Stub { get; }
        public IDalamudPluginInterface PluginInterface { get; }
        public DalamudVersionInfo DalamudVersion { get; }
        private IDataManager Data { get; }
        private ILogger Logger { get; }

        public SonarPluginIoC(SonarPluginStub stub, IDalamudPluginInterface pluginInterface)
        {
            this.Stub = stub;
            this.PluginInterface = pluginInterface;

            this._container = this.CreateContainer();

            this.Data = this._container.Resolve<IDataManager>();
            this.DalamudVersion = this._container.Resolve<DalamudVersionInfo>();
            this.Logger = this._container.Resolve<ILogger<SonarPluginIoC>>();
        }

        private SonarClient CreateSonarClient()
        {
            this.Logger.LogInformation("Initializing Sonar");
            var startInfo = new SonarStartInfo()
            {
                WorkingDirectory = Path.Join(this.PluginInterface.GetPluginConfigDirectory(), "Sonar"),
                PluginSecretMeta = SecretUtils.GetSecretMetaBytes(typeof(SonarPlugin).Assembly),
                ChallengeHandler = this.ChallengeHandlerAsync
            };

            SonarLanguage DetermineLanguage(int num)
            {
                // TC(台服)客戶端在 Dalamud 13.0.0.16 之後回報 ClientLanguage 7(TraditionalChinese),
                // 舊版回報 4(ChineseSimplified)。用數值比較才能同時相容 CI 釘的 13.0.0.6(列舉沒有 7 這個名字)與執行期新版。
                var name = Enum.GetName((ClientLanguage)num);
                if (name is "Korean") return SonarLanguage.Korean;
                if (name is "ChineseSimplified") return SonarLanguage.ChineseSimplified;
                if (name is "ChineseTraditional") return SonarLanguage.ChineseSimplified; // TODO: Change to .ChineseTraditional once done
                if (name is "TraditionalChinese") return SonarLanguage.ChineseSimplified; // TODO: Change to .ChineseTraditional once done

                this.Logger.LogWarning("Unable to determine ClientLanguage: {num}", num);
                return
                    num is 4 ? SonarLanguage.ChineseSimplified :
                    num is 5 ? SonarLanguage.ChineseSimplified : // TODO: Change to .ChineseTraditional once done
                    num is 7 ? SonarLanguage.ChineseSimplified : // TODO: Change to .ChineseTraditional once done
                    SonarLanguage.English;
            }

            var versionInfo = VersionUtils.GetSonarVersionModel(this.Data, this.PluginInterface, this.DalamudVersion);
            var client = new SonarClient(startInfo) { VersionInfo = versionInfo };
            Database.DefaultLanguage = this.Data.Language switch
            {
                ClientLanguage.Japanese => SonarLanguage.Japanese,
                ClientLanguage.English => SonarLanguage.English,
                ClientLanguage.German => SonarLanguage.German,
                ClientLanguage.French => SonarLanguage.French,
                _ => DetermineLanguage((int)this.Data.Language), // https://github.com/ottercorp/Dalamud/blob/cn/Dalamud/ClientLanguage.cs#L31 https://github.com/yanmucorp/Dalamud/blob/master/Dalamud/Game/ClientLanguage.cs#L36
            };
            return client;
        }

        private async Task<IReadOnlyDictionary<string, ImmutableArray<byte>>?> ChallengeHandlerAsync(ImmutableArray<byte> key, CancellationToken cancellationToken)
        {
            var directory = this.PluginInterface.AssemblyLocation.Directory;
            if (directory is null) return null;

            var results = new Dictionary<string, ImmutableArray<byte>>();
            await foreach (var (file, result) in SonarIntegrity.GenerateHashesAsync(directory, key.AsMemory(), cancellationToken))
            {
                results.Add(file, result);
            }
            return results;
        }

        /// <summary>
        /// IDalamudPluginInterface.GetRequiredService&lt;T&gt;() (an IServiceProvider-style extension) does not exist
        /// at this Dalamud API level. Instead, resolve everything at once via IDalamudPluginInterface.Create&lt;T&gt;(),
        /// which fills [PluginService] properties the same way [PluginService] field injection does elsewhere.
        /// </summary>
        private sealed class DalamudServiceBag
        {
            [PluginService] public IPluginLog PluginLog { get; set; } = null!;
            [PluginService] public IFramework Framework { get; set; } = null!;
            [PluginService] public ICondition Condition { get; set; } = null!;
            [PluginService] public IClientState ClientState { get; set; } = null!;
            [PluginService] public IGameGui GameGui { get; set; } = null!;
            [PluginService] public IChatGui ChatGui { get; set; } = null!;
            [PluginService] public ICommandManager CommandManager { get; set; } = null!;
            [PluginService] public IFateTable FateTable { get; set; } = null!;
            [PluginService] public IObjectTable ObjectTable { get; set; } = null!;
            [PluginService] public ISigScanner SigScanner { get; set; } = null!;
            [PluginService] public IDataManager DataManager { get; set; } = null!;
            [PluginService] public ITextureProvider TextureProvider { get; set; } = null!;
        }

        private Container CreateContainer()
        {
            var container = new Container();
            container.RegisterInstanceMany(container, setup: Setup.With(preventDisposal: true));

            var dalamudServices = this.PluginInterface.Create<DalamudServiceBag>() ?? throw new InvalidOperationException("Failed to inject Dalamud services");

            // Services
            container.RegisterExports(typeof(SonarPluginIoC).Assembly, typeof(SonarEventManager).Assembly);

            // Logging Services
            container.RegisterInstance(dalamudServices.PluginLog, setup: Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(() => new LoggerFactory(Arg.Of<IEnumerable<ILoggerProvider>>())), Reuse.Singleton);
            container.Register(typeof(ILogger<>), typeof(PluginLoggerAdapter<>), Reuse.Singleton);
            container.AddPluginLogger();

            // SonarPlugin services
            container.RegisterInstance(this, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(this.Stub, setup: Setup.With(preventDisposal: true));

            // Sonar Services
            container.RegisterDelegate(this.CreateSonarClient, Reuse.Singleton);
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<SonarClient>(), client => client.Trackers), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<SonarClient>(), client => client.Configuration), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<SonarClient>(), client => client.Meta), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<RelayTrackers>(), trackers => trackers.Hunts), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<RelayTrackers>(), trackers => trackers.Fates), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<SonarPlugin>(), plugin => plugin.Windows), Reuse.Singleton, Setup.With(preventDisposal: true));

            // Additional Services
            container.RegisterDelegate(this.GetOrCreateFileDialogManager, Reuse.Singleton);
            container.RegisterInstanceMany(PlaceholderFormatter.Default);

            // Dalamud Services
            // NOTE: IPlayerState doesn't exist at this Dalamud API level (and nothing in this plugin consumes it
            // via DI) so its registration was dropped along with the rest of the GetRequiredService<T> calls.
            container.RegisterInstance(this.PluginInterface, setup: Setup.With(preventDisposal: true)); // Dispose is [Obsolete]
            container.RegisterInstance(dalamudServices.Framework, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.Condition, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.ClientState, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.GameGui, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.ChatGui, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.CommandManager, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.FateTable, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.ObjectTable, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.SigScanner, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.DataManager, setup: Setup.With(preventDisposal: true));
            container.RegisterInstance(dalamudServices.TextureProvider, setup: Setup.With(preventDisposal: true));
            // Dalamud.Plugin.VersionInfo.IDalamudVersionInfo / IDalamudPluginInterface.GetDalamudVersion() do not
            // exist at this Dalamud API level; use our own DalamudVersionInfo (SonarPlugin.Utility) instead.
            container.RegisterInstance(new DalamudVersionInfo(), setup: Setup.With(preventDisposal: true));

            // Additional Dalamud Services
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<IDalamudPluginInterface>(), pluginInterface => pluginInterface.UiBuilder), Reuse.Singleton, Setup.With(preventDisposal: true));
            container.RegisterMany(Made.Of(request => ServiceInfo.Of<IDataManager>(), data => data.GameData), Reuse.Singleton, Setup.With(preventDisposal: true));

#if DEBUG
            container.PerformDebugValidation(out _, container.Resolve<ILogger<SonarPluginIoC>>());
#endif

            return container;
        }

        private FileDialogManager GetOrCreateFileDialogManager()
        {
            if (this._fileDialogs is null && Interlocked.CompareExchange(ref this._fileDialogs, new(), null) is null)
            {
                this.PluginInterface.UiBuilder.Draw += this._fileDialogs.Draw;
            }
            return this._fileDialogs;
        }

        public void StartServices()
        {
            // Called from a backgrounded task (see SonarPluginStub), so blocking here is fine.
            this._container.StartAllServicesAsync(this.Logger).Wait();
        }

        public void StopServices()
        {
            // Called synchronously from Dispose() (plugin unload/disable), which Dalamud invokes
            // on the main thread and expects to return promptly. Bound the wait so a slow/hung
            // network teardown can't freeze the game indefinitely; the container is disposed
            // right after regardless, which cleans up anything left dangling.
            if (!this._container.StopAllServicesAsync(this.Logger).Wait(TimeSpan.FromSeconds(3)))
            {
                this.Logger.LogWarning("Timed out waiting for Sonar services to stop; continuing with disposal");
            }
        }

        public void Dispose()
        {
            if (this._fileDialogs is not null) this.PluginInterface.UiBuilder.Draw -= this._fileDialogs.Draw;
            this._container.Dispose(); // All singleton disposables are disposed here
        }
    }
}
