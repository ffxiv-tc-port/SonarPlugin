using Sonar.Extensions;
using Sonar.Messages;
using Sonar.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sonar.Connections
{
    public sealed partial class SonarConnectionManager
    {
        internal bool SendIfConnected(ISonarMessage message)
        {
            var socket = this._socket;
            if (socket is null) return false;
            try
            {
                socket.Send(message.SerializeClientToServer());
                return true;
            }
            catch
            {
                /* Swallow exception */
                return false;
            }
        }

        internal bool SendIfConnected(Func<ISonarMessage> messageFactory)
        {
            if (this._socket is null) return false;
            return this.SendIfConnected(messageFactory()); // It still possible this return false if disconnected while running the factory
        }

        /// <summary>Starts the url list download and the connect loop. Returns <see langword="false"/> if already started or disposed.</summary>
        /// <remarks>Nothing leaves this machine before the first call: construction only reads embedded data.</remarks>
        internal bool Start()
        {
            lock (this._runLock)
            {
                if (this._disposed || this._runCts is not null) return false;
                this._failCount = 0;
                this._reconnect = false;
                var run = CancellationTokenSource.CreateLinkedTokenSource(this._cts.Token);
                this._runCts = run;
                this._urls.StartBootstrap();
                _ = this.ConnectLoopTask(run.Token);
            }
            return true;
        }

        /// <summary>Stops connecting, drops every connection and the url list download. Returns <see langword="false"/> if not started.</summary>
        /// <remarks>Does not block on socket teardown, so it is safe to call from the UI thread. <see cref="Start"/> may be called again afterwards.</remarks>
        internal bool Stop()
        {
            CancellationTokenSource? run;
            ISonarSocket? active;
            lock (this._runLock)
            {
                run = this._runCts;
                if (run is null) return false;
                this._runCts = null;
                active = Interlocked.Exchange(ref this._socket, null);
            }

            // Not disposed: in-flight attempts may still link to its token. It holds no unmanaged resource.
            try { run.Cancel(); } catch (AggregateException) { /* Swallow */ }
            this._urls.StopBootstrap();

            var sockets = this._sockets.Keys.ToArray();
            foreach (var socket in sockets) this.DetachSocket(socket);
            if (active is not null)
            {
                this.Client.LogInformation("Sonar connection stopped");
                this.DisconnectedInternal?.Invoke(this, active);
                if (this.Disconnected is not null) _ = Task.Run(() => this.Disconnected?.SafeInvoke(this));
            }
            _ = Task.Run(() =>
            {
                foreach (var socket in sockets)
                {
                    try { socket.Dispose(); } catch { /* Swallow */ }
                }
            });
            return true;
        }
    }
}
