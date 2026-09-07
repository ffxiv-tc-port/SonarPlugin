using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DryIocAttributes;
using Sonar;
using Sonar.Enums;
using Sonar.Extensions;
using Sonar.Logging;
using SonarPlugin.Config;
using SonarPlugin.Utility;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SonarPlugin
{
    [ExportMany]
    [SingletonReuse]
    public sealed class SonarPlugin : IDisposable
    {
        private IDalamudPluginInterface PluginInterface { get; }
        private SonarClient Client { get; }
        private ChatQueue Chat { get; }
        private AudioPlaybackEngine Audio { get; }
        private IPluginLog Logger { get; }

        public SonarPlugin(IDalamudPluginInterface pluginInterface, SonarClient client, ChatQueue chat, AudioPlaybackEngine audio, IPluginLog logger)
        {
            this.PluginInterface = pluginInterface;
            this.Client = client;
            this.Chat = chat;
            this.Audio = audio;
            this.Logger = logger;

            this.Initialize();
        }

        public WindowSystem Windows { get; } = new(nameof(SonarPlugin));
        public SonarConfiguration Configuration { get; private set; } = default!;

        public void Initialize()
        {
            this.LoadConfiguration();

            this.Logger.Info("Setting up localization");
            EnumLocUtils.Setup(this.Configuration.Localization.DebugFallbacks);

            // TC fork: default new installs to the Traditional Chinese preset once; users may change language afterwards.
            if (!this.Configuration.TcLanguageDefaultApplied)
            {
                this.Logger.Info("Applying Traditional Chinese language default (TC fork, one time)");
                this.Configuration.TcLanguageDefaultApplied = true;
                // A brand new install lands on the full preset here, so the EnumLoc migration below is already satisfied.
                this.Configuration.TcEnumLocMigrationApplied = true;
                this.Configuration.Localization.Preset = LocalizationPreset.ChineseTraditional; // also applies CheapLoc, see LocalizationConfig.SetPresetCore
                this.SaveConfiguration();
            }
            else if (!this.Configuration.TcEnumLocMigrationApplied)
            {
                // TC fork: one-time migration for users who installed before the zh-TW AG.EnumLocalization language
                // files existed. Their config stored the Traditional Chinese preset as (ChineseTraditional, null,
                // null); the plugin/Sonar EnumLoc strings therefore stayed on the English fallback until they re-picked
                // a language by hand. Only that exact old-default shape is upgraded, so a user who has since chosen a
                // different language (or a different Db) is never overridden. Set the flag first so the migration never
                // repeats, and guard the whole thing so a failure fails safe (stays on the current strings, no crash).
                this.Configuration.TcEnumLocMigrationApplied = true;
                try
                {
                    var loc = this.Configuration.Localization;
                    if (loc.Db == SonarLanguage.ChineseTraditional && loc.Plugin is null && loc.Sonar is null)
                    {
                        this.Logger.Information("Migrating existing Traditional Chinese config to the zh-TW EnumLoc language files (TC fork, one time)");
                        loc.Preset = LocalizationPreset.ChineseTraditional; // sets Plugin/Sonar language files + applies CheapLoc, see LocalizationConfig.SetPresetCore
                    }
                    else
                    {
                        // User already chose a language (or a non-default Db); leave their selection alone.
                        EnumLocUtils.ApplyCheapLoc(loc.Preset);
                    }
                }
                catch (Exception ex)
                {
                    this.Logger.Warning(ex, "zh-TW EnumLoc migration failed; keeping current language (TC fork)");
                }
                this.SaveConfiguration();
            }
            else
            {
                EnumLocUtils.ApplyCheapLoc(this.Configuration.Localization.Preset);
            }

            this.Logger.Info("SonarPlugin Resources:");
            foreach (var resourceName in typeof(SonarPlugin).Assembly.GetManifestResourceNames())
            {
                this.Logger.Info($" - {resourceName}");
            }

            this.Logger.Info("Sonar Resources:");
            foreach (var resourceName in typeof(SonarClient).Assembly.GetManifestResourceNames())
            {
                this.Logger.Info($" - {resourceName}");
            }

            this.PluginInterface.UiBuilder.Draw += this.Windows.Draw;

            // Set volume of alerts to current config, this also will initialize the Instance of the audio service
            this.Audio.Volume = this.Configuration.SoundVolume;

            // Start Sonar.NET client
            this.Client.ServerMessage += this.Events_OnSonarMessage;
            this.Client.LogMessage += this.ClientLogHandler;
            this.Client.Start();
        }

        private void Events_OnSonarMessage(SonarClient source, string? message)
        {
            if (message is null) return;
            // Server messages arrive on the socket receive loop, and IChatGui may only be used
            // from the framework thread, so the line goes through the queue that ChatQueue
            // drains there. Queued rather than hopped per line so consecutive messages keep
            // their order.
            var entry = new XivChatEntry
            {
                Type = this.Configuration.HuntOutputChannel,
                Name = "Sonar",
                Message = message
            };
            this.Chat.Print(entry);
            this.Logger.Information("Sonar Message Received: {message}");
        }

        [SuppressMessage("Major Code Smell", "S112", Justification = "No suitable exception")]
        public void LoadConfiguration(bool isReset = false)
        {
            try
            {
                var configuration = (SonarConfiguration?)this.PluginInterface.GetPluginConfig();
                if (configuration is null)
                {
                    if (isReset) throw new Exception($"Failed resetting configuration");
                    this.ResetConfiguration();
                    return;
                }
                this.Configuration = configuration;

                this.Configuration.Sanitize();
                this.Client.Configuration.ReadFrom(this.Configuration.SonarConfig);
                this.Configuration.SonarConfig = this.Client.Configuration;

                if (this.Configuration.PerformVersionUpdate(this.Logger))
                {
                    this.SaveConfiguration(true);
                }
            }
            catch (Exception ex)
            {
                this.Logger.Error($"Failed to load configuration: {ex}");
                if (!isReset) this.ResetConfiguration();
            }
        }

        public void SaveConfiguration(bool updateServer = false)
        {
            try
            {
                //if (updateServer) this.Client.Configuration = this.Client.Configuration; // TODO: Better interface than assigning (using the setter)...
                this.Configuration.SonarConfig = this.Client.Configuration;
                this.PluginInterface.SavePluginConfig(this.Configuration);
            }
            catch (Exception e)
            {
                this.Logger.Error($"Failed to save configuration: {e}");
            }
        }

        public void ResetConfiguration()
        {
            this.Configuration = new SonarConfiguration();
            this.Client.Configuration.ReadFrom(this.Configuration.SonarConfig);
            this.Configuration.SonarConfig = this.Client.Configuration;
            this.Client.Configuration.Contribute.Reset();
            this.SaveConfiguration(true);
            this.LoadConfiguration(true);
        }

        private void ClientLogHandler(SonarClient source, SonarLogMessage log) => this.LogHandler(log);

        [SuppressMessage("Minor Code Smell", "S3458", Justification = "Clarity")]
        private void LogHandler(SonarLogMessage log)
        {
            var (level, message) = (log.Level, log.Message);
            switch (level)
            {
                case SonarLogLevel.Verbose:
                    this.Logger.Verbose(message);
                    break;
                case SonarLogLevel.Debug:
                    this.Logger.Debug(message);
                    break;
                case SonarLogLevel.Information:
                    this.Logger.Information(message);
                    break;
                case SonarLogLevel.Warning:
                    this.Logger.Warning(message);
                    break;
                case SonarLogLevel.Error:
                    this.Logger.Error(message);
                    break;
                case SonarLogLevel.Fatal:
                default:
                    this.Logger.Fatal(message);
                    break;
            }
        }

        #region IDisposable Support
        private bool _disposed; // Interlocked
        public bool IsDisposed => this._disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref this._disposed, true, false)) return;

            this.SaveConfiguration();

            // Hunt and Fate Trackers
            if (this.Client is not null)
            {
                this.Client.ServerMessage -= this.Events_OnSonarMessage;
                this.Client.LogMessage -= this.ClientLogHandler;
            }

            if (this.PluginInterface is not null)
            {
                // Logged in / out handlers
                this.PluginInterface.UiBuilder.Draw -= this.Windows.Draw;
            }
        }
        #endregion
    }
}
