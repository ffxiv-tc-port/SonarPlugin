using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SonarPlugin
{
    /// <summary>
    /// Chat output funnel: lines may be queued from any thread and are written on the framework
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><see cref="IChatGui"/>'s Print methods hand the message to a queue that only the
    /// framework thread drains, so calling them from a background thread is not safe. Sonar
    /// prints from the tracker tasks, from the socket receive loop and from the load/unload
    /// tasks, so those calls go through here instead.</para>
    /// <para>One queue drained from <see cref="IFramework.Update"/> on purpose, rather than one
    /// <see cref="IFramework.RunOnFrameworkThread(Action)"/> per line: that schedules every line
    /// as its own task, and the scheduler behind it makes no ordering promise, so lines produced
    /// back to back can come out swapped. A single FIFO keeps them in the order they were
    /// produced no matter which thread produced them.</para>
    /// <para>Owned by <see cref="SonarPluginStub"/> and shared with the loaded plugin through the
    /// IoC container, so it survives a reload.</para>
    /// </remarks>
    public sealed class ChatQueue : IDisposable
    {
        private readonly ConcurrentQueue<Line> _lines = new();
        private int _disposed; // Interlocked

        private IChatGui Chat { get; }
        private IFramework Framework { get; }
        private IPluginLog Logger { get; }

        public ChatQueue(IChatGui chat, IFramework framework, IPluginLog logger)
        {
            this.Chat = chat;
            this.Framework = framework;
            this.Logger = logger;

            this.Framework.Update += this.Framework_Update;
        }

        /// <summary>Queues <paramref name="entry"/> for <see cref="IChatGui.Print(XivChatEntry)"/>.</summary>
        public void Print(XivChatEntry entry) => this._lines.Enqueue(new(entry, null));

        /// <summary>Queues <paramref name="message"/> for <c>IChatGui.PrintError</c>.</summary>
        public void PrintError(string message) => this._lines.Enqueue(new(null, message));

        private void Framework_Update(IFramework framework) => this.Drain();

        private void Drain()
        {
            while (this._lines.TryDequeue(out var line))
            {
                try
                {
                    if (line.Error is not null) this.Chat.PrintError(line.Error);
                    else if (line.Entry is not null) this.Chat.Print(line.Entry);
                }
                catch (Exception ex)
                {
                    // One bad line must not stop the rest of the queue from draining.
                    this.Logger.Error(ex, "Exception occurred while printing a queued chat message");
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) is not 0) return;
            this.Framework.Update -= this.Framework_Update;

            // The unload failure report is queued moments before this runs, and Dalamud disposes
            // plugins on the framework thread, so flush what is still queued instead of dropping
            // it. Dalamud runs the action inline while the caller is on that thread or while the
            // framework is unloading, and one hop for the whole queue keeps the lines in order.
            _ = this.Framework.RunOnFrameworkThread(this.Drain);
        }

        /// <summary>A queued chat line. Exactly one of the two members is set.</summary>
        private readonly record struct Line(XivChatEntry? Entry, string? Error);
    }
}
