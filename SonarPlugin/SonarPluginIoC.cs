using Sonar;
using System;
using DryIoc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Plugin;
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
        private DalamudServices Services { get; }
        private IDataManager Data { get; }
        private ILogger Logger { get; }

        public SonarPluginIoC(SonarPluginStub stub, IDalamudPluginInterface pluginInterface)
        {
            this.Stub = stub;
            this.PluginInterface = pluginInterface;
            this.Services = pluginInterface.Create<DalamudServices>()!;

            this._container = this.CreateContainer();

            this.Data = this._container.Resolve<IDataManager>();
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
                var name = Enum.GetName((ClientLanguage)num);
                if (name is "Korean") return SonarLanguage.Korean;
                if (name is "ChineseSimplified") return SonarLanguage.ChineseSimplified;
                if (name is "ChineseTraditional") return SonarLanguage.ChineseSimplified; // TODO: Change to .ChineseTraditional once done

                this.Logger.LogWarning("Unable to determine ClientLanguage: {num}", num);
                return
                    num is 4 ? SonarLanguage.ChineseSimplified :
                    num is 5 ? SonarLanguage.ChineseSimplified : // TODO: Change to .ChineseTraditional once done
                    SonarLanguage.English;
            }

            var versionInfo = VersionUtils.GetSonarVersionModel(this.Data, this.PluginInterface);
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

        private Container CreateContainer()
        {
            var container = new Container();
            container.RegisterInstanceMany(container, setup: Setup.With(preventDisposal: true));

            // Services
            container.RegisterExports(typeof(SonarPluginIoC).Assembly, typeof(SonarEventManager).Assembly);

            // Logging Services
            container.RegisterInstance(this.Services.PluginLog, setup: Setup.With(preventDisposal: true));
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
            container.RegisterInstance(this.PluginInterface, setup: Setup.With(preventDisposal: true)); // Dispose is [Obsolete]
            container.RegisterDelegate(() => this.Services.Framework, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.Condition, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.ClientState, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.GameGui, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.ChatGui, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.CommandManager, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.FateTable, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.ObjectTable, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.SigScanner, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.DataManager, Reuse.Singleton, setup: Setup.With(preventDisposal: true));
            container.RegisterDelegate(() => this.Services.TextureProvider, Reuse.Singleton, setup: Setup.With(preventDisposal: true));

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
            this._container.StartAllServicesAsync(this.Logger).Wait();
        }

        public void StopServices()
        {
            this._container.StopAllServicesAsync(this.Logger).Wait();
        }

        public void Dispose()
        {
            if (this._fileDialogs is not null) this.PluginInterface.UiBuilder.Draw -= this._fileDialogs.Draw;
            this._container.Dispose(); // All singleton disposables are disposed here
        }
    }
}
