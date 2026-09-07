using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DryIoc;
using SonarPlugin.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SonarPlugin
{
    public sealed class SonarPluginStub : IDalamudPlugin
    {
        private readonly Lock _pluginLock = new(); // Load and unload lock
        private bool _disposed; // Interlocked

        /// <summary>Sonar name</summary>
        public string Name { get; } = "Sonar";

        /// <summary>Sonar name</summary>
        public string PluginName { get; } = "SonarPlugin";

        /// <summary>Sonar flavor</summary>
        public string? Flavor { get; }

        /// <summary>SonarPlugin's IoC class.</summary>
        private SonarPluginIoC? Plugin;

        private IDalamudPluginInterface PluginInterface { get; }
        private ICommandManager Commands { get; }
        private IPluginLog Logger { get; }

        /// <summary>Chat output funnel, shared with the loaded plugin through the IoC container.</summary>
        public ChatQueue ChatQueue { get; }

        public SonarPluginStub(IDalamudPluginInterface pluginInterface, ICommandManager commands, IChatGui chat, IPluginLog logger, IFramework framework)
        {
            this.PluginInterface = pluginInterface;
            this.Commands = commands;
            this.Logger = logger;
            this.ChatQueue = new(chat, framework, logger);
            
            this.Logger.Debug("Initializing Sonar [Stub]");
            this.PluginInterface = pluginInterface;
            this.PluginInterface.Inject(this);

            try
            {
                var flavor = FlavorUtils.DetermineFlavor(pluginInterface, logger);
                if (!string.IsNullOrWhiteSpace(flavor))
                {
                    this.Logger.Information($"Detected Flavor: {flavor}");
                    this.Name = $"{this.Name}-{flavor}";
                    this.PluginName = $"{this.PluginName}-{flavor}";
                    this.Flavor = flavor;
                }
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "Exception occured while getting flavor");
            }

            this.Commands.AddHandler("/sonarload", new CommandInfo(this.SonarLoadCommand) { HelpMessage = "Turn on / enable Sonar", ShowInHelp = false });
            this.Commands.AddHandler("/sonarunload", new CommandInfo(this.SonarUnloadCommand) { HelpMessage = "Turn off / disable Sonar", ShowInHelp = false });
            this.Commands.AddHandler("/sonarreload", new CommandInfo(this.SonarReloadCommand) { HelpMessage = "Reload Sonar", ShowInHelp = false });

            // Constructed synchronously by Dalamud on plugin load; InitializeSonar() blocks on
            // network connect via StartServices(), so it must not run inline here. Same
            // backgrounding pattern already used by SonarLoadCommand below.
            Task.Factory.StartNew(this.InitializeSonar, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        private void SonarLoadCommand(string? _ = null, string? __ = null)
        {
            this.PrintErrorOnFramework("WARNING: /sonarload, /sonarunload and /sonarreload are not yet fixed! Use /sonaron, /sonaroff, /sonarenable and /sonardisable instead.");
            Task.Factory.StartNew(this.InitializeSonar, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        private void SonarUnloadCommand(string? _ = null, string? __ = null)
        {
            this.PrintErrorOnFramework("WARNING: /sonarload, /sonarunload and /sonarreload are not yet fixed! Use /sonaron, /sonaroff, /sonarenable and /sonardisable instead.");
            Task.Factory.StartNew(this.DestroySonar, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        private void SonarReloadCommand(string? _ = null, string? __ = null)
        {
            this.PrintErrorOnFramework("WARNING: /sonarload, /sonarunload and /sonarreload are not yet fixed! Use /sonaron, /sonaroff, /sonarenable and /sonardisable instead.");
#if !DEBUG
            return; // TODO: Remove once fixed
#endif
            Task.Factory.StartNew(this.ReloadSonar, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        private void InitializeSonar()
        {
            if (Volatile.Read(ref this._disposed)) return;

            // Lines produced while _pluginLock is held are recorded here and written once it is
            // released - see PendingLine. Level, text and trigger are unchanged; only the moment
            // of the write moved out of the lock.
            List<PendingLine> pending = [];
            lock (this._pluginLock)
            {
                if (this.Plugin is not null) return; // Nothing recorded yet, nothing to flush.
                try
                {
                    pending.Add(new(PendingLineKind.LogDebug, null, "Starting Sonar", []));
                    this.Plugin = new(this, this.PluginInterface);
                    this.Plugin.StartServices();
                }
                catch (Exception ex)
                {
                    pending.Add(new(PendingLineKind.LogError, ex, string.Empty, []));
                    BuildErrorReport(pending, ex, "initialized", true);

                    if (ex is ContainerException cex && this.Plugin is not null)
                    {
                        // Formatted here on purpose: it reads this.Plugin.Container, which is only
                        // stable while the lock is held. Only the write itself is deferred.
                        pending.Add(new(PendingLineKind.LogError, null, cex.TryGetDetails(this.Plugin.Container), []));
                    }
                    /* Swallow Exception */
                }
            }

            this.EmitPending(pending);
        }

        private void DestroySonar()
        {
            List<PendingLine> pending = [];
            lock (this._pluginLock)
            {
                if (this.Plugin is null) return; // Nothing recorded yet, nothing to flush.
                try
                {
                    pending.Add(new(PendingLineKind.LogDebug, null, "Stopping Sonar", []));
                    // Recorded rather than written for the same reason as everything else in
                    // here: StopServices runs with _pluginLock held.
                    if (!this.Plugin.StopServices()) pending.Add(new(PendingLineKind.LogWarning, null, "Timed out waiting for Sonar services to stop; continuing with disposal", []));
                    this.Plugin.Dispose();
                    this.Plugin = null;
                }
                catch (Exception ex)
                {
                    BuildErrorReport(pending, ex, "disposed", false);
                    pending.Add(new(PendingLineKind.LogError, ex, string.Empty, []));
                    if (ex is ContainerException cex)
                    {
                        // Formatted here on purpose, same reason as InitializeSonar above.
                        pending.Add(new(PendingLineKind.LogError, null, cex.TryGetDetails(this.Plugin!.Container), []));
                    }
                    /* Swallow Exception */
                }
            }

            this.EmitPending(pending);
        }

        private void ReloadSonar()
        {
            this.DestroySonar();
            this.InitializeSonar();
        }

        /// <summary>
        /// A log or chat line produced while <see cref="_pluginLock"/> was held.
        /// </summary>
        /// <remarks>
        /// <para><see cref="IPluginLog"/> ends up in Dalamud's Serilog sink, which does file I/O
        /// and takes locks of its own, and <see cref="IChatGui"/> hands work over to the framework
        /// thread. Holding the load/unload lock across either of those puts every other loader
        /// behind them, so the write is deferred until the lock is released instead.</para>
        /// <para>Template and values are kept apart rather than pre-formatted, so the structured
        /// logging properties come out exactly as they did before.</para>
        /// <para>Deliberately pure data: the write lives in <see cref="EmitPending"/> so that
        /// nothing reachable from inside the lock can log, not even by accident.</para>
        /// </remarks>
        private readonly record struct PendingLine(PendingLineKind Kind, Exception? Exception, string Template, object[] Values);

        private enum PendingLineKind
        {
            LogDebug,
            LogWarning,
            LogError,
            ChatError,
        }

        /// <summary>Writes deferred lines. Must be called with <see cref="_pluginLock"/> released.</summary>
        private void EmitPending(List<PendingLine> pending)
        {
            foreach (var line in pending)
            {
                switch (line.Kind)
                {
                    case PendingLineKind.LogDebug:
                        this.Logger.Debug(line.Exception, line.Template, line.Values);
                        break;
                    case PendingLineKind.LogWarning:
                        this.Logger.Warning(line.Exception, line.Template, line.Values);
                        break;
                    case PendingLineKind.LogError:
                        this.Logger.Error(line.Exception, line.Template, line.Values);
                        break;
                    case PendingLineKind.ChatError:
                        this.PrintErrorOnFramework(line.Template);
                        break;
                }
            }
        }

        /// <summary>
        /// Queues an error line to be printed on the framework thread. <see cref="IChatGui"/>'s
        /// Print methods enqueue onto a queue that only the framework thread drains, and this
        /// class talks to chat from background tasks (load and unload both run on one). The
        /// queue also keeps consecutive lines in order, which one hop per line would not - see
        /// <c>ChatQueue</c>.
        /// </summary>
        private void PrintErrorOnFramework(string message) => this.ChatQueue.PrintError(message);

        public void ShowError(Exception ex, string action = "initialized", bool isAsync = false)
        {
            List<PendingLine> pending = [];
            BuildErrorReport(pending, ex, action, isAsync);
            this.EmitPending(pending);
        }

        /// <summary>
        /// Records the lines <see cref="ShowError"/> writes, without writing them. Callers holding
        /// <see cref="_pluginLock"/> use this and flush with <see cref="EmitPending"/> after the
        /// lock is released; level, text and order are the same either way.
        /// </summary>
        private static void BuildErrorReport(List<PendingLine> pending, Exception ex, string action, bool isAsync)
        {
            var header = $"Sonar could not be {action} {(isAsync ? "in async context" : "")}";
            var dalamud = "Check /xllog for more information";
            var footer = "Sonar may be in an undefined state, a game restart may be required.";
            var contact = "If this problem persist report it to https://discord.gg/K7y24Rr";

            pending.Add(new(PendingLineKind.LogError, null, header, []));
            pending.Add(new(PendingLineKind.ChatError, null, header, []));

            try { if (ex is AggregateException aex) ex = aex.Flatten(); } catch { /* Swallow */ }
            pending.Add(new(PendingLineKind.LogError, null, $"{ex}", []));
            try
            {
                if (ex is ReflectionTypeLoadException rexs)
                {
                    foreach (var rex in rexs.LoaderExceptions.Where(ex => ex is not null))
                    {
                        var rrex = rex;
                        try { if (rrex is AggregateException aex) rrex = aex.Flatten(); } catch { /* Swallow */ }
                        pending.Add(new(PendingLineKind.LogError, null, $"{rrex}", []));
                    }
                }
            }
            catch (Exception ex2) { pending.Add(new(PendingLineKind.LogError, ex2, string.Empty, [])); }
            pending.Add(new(PendingLineKind.ChatError, null, dalamud, []));

            pending.Add(new(PendingLineKind.LogError, null, footer, []));
            pending.Add(new(PendingLineKind.ChatError, null, footer, []));

            pending.Add(new(PendingLineKind.LogError, null, contact, []));
            pending.Add(new(PendingLineKind.ChatError, null, contact, []));
        }

        [SuppressMessage("Minor Code Smell", "S3458", Justification = "Clarity")]
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref this._disposed, true, false)) return;

            this.Commands.RemoveHandler("/sonaron");
            this.Commands.RemoveHandler("/sonarenable");

            this.Commands.RemoveHandler("/sonaroff");
            this.Commands.RemoveHandler("/sonardisable");

            this.Commands.RemoveHandler("/sonarreload");

            this.DestroySonar();

            // After DestroySonar, so the report it queues on failure still gets flushed.
            this.ChatQueue.Dispose();

            GC.SuppressFinalize(this);
        }

        ~SonarPluginStub()
        {
            try { this.Dispose(); } catch (Exception ex) { this.Logger.Error(ex, string.Empty); }
        }
    }
}
