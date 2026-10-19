using Sonar.Logging;
using SonarUtils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Sonar.Connections
{
    /// <summary>Manages Sonar connection URLs</summary>
    internal sealed partial class SonarUrlManager : IDisposable, IAsyncDisposable
    {
        /// <summary>Budget for one bootstrap request: connect, TLS and a few KB of url list.</summary>
        // HttpClient's 100 second default dwarfed the 15 second connect budget used for
        // actual connections, so a single stalled request stalled the refresh loop with it.
        private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(30);

        private readonly ISonarLogger _logger;
        private readonly Lock _bootstrapLock = new();
        private CancellationTokenSource? _bootstrapCts;
        private Task _bootstrapTask = Task.CompletedTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly NonBlocking.NonBlockingHashSet<SonarUrl> _urls = [];
        private SonarUrl[]? _urlArray;

        private SonarUrl[] GetSonarUrls() => this._urlArray ??= [.. this._urls];

        /// <remarks>Makes no network request: the url list download only runs between <see cref="StartBootstrap"/> and <see cref="StopBootstrap"/>.</remarks>
        public SonarUrlManager(ISonarLogger logger)
        {
            this._logger = logger;
            this.ReadEmbeddedUrls();
        }

        /// <summary>Starts the periodic url list download. No effect if already running or disposed.</summary>
        public void StartBootstrap()
        {
            lock (this._bootstrapLock)
            {
                if (this._bootstrapCts is not null || this._cts.IsCancellationRequested) return;
                var cts = CancellationTokenSource.CreateLinkedTokenSource(this._cts.Token);
                this._bootstrapCts = cts;
                this._bootstrapTask = Task.Run(async () =>
                {
                    try { await this.BootstrapWebTask(cts.Token).ConfigureAwait(false); }
                    finally { cts.Dispose(); }
                });
            }
        }

        /// <summary>Stops the periodic url list download. Already downloaded urls stay in use.</summary>
        public void StopBootstrap()
        {
            CancellationTokenSource? cts;
            lock (this._bootstrapLock)
            {
                cts = this._bootstrapCts;
                this._bootstrapCts = null;
            }
            // The download task disposes its own token source when it ends, which can race this.
            try { cts?.Cancel(); } catch (ObjectDisposedException) { /* Swallow */ } catch (AggregateException) { /* Swallow */ }
        }

        /// <param name="proxy">Allow proxy URLs</param>
        /// <param name="reconnect">Allow reconnect only restricted URLs, for reconnects only</param>
        /// <param name="retries">Number of retries to return a URL with specified properties. Should this happen a completely random URL is returned instead. (You must really have bad luck for this to happen)</param>
        [SuppressMessage("Security", "CA5394", Justification = "Not applicable.")] // Used for url randomization
        public SonarUrl GetRandomUrl(bool proxy = false, bool reconnect = false, int retries = 255)
        {
            var urls = this.GetSonarUrls();
            var random = SonarStatic.Random;
            var loop = 0;
            while (true)
            {
                var url = urls[random.Next(urls.Length)];
                if ((loop++ < retries) && ((url.Proxy && !proxy) || (url.ReconnectOnly && !reconnect))) continue;
                return url;
            }
        }

        /// <summary>Read URLs from embedded resource</summary>
        private void ReadEmbeddedUrls()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("Sonar.Resources.Urls.data")
                ?? throw new FileNotFoundException($"Couldn't read url resources");

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes, 0, bytes.Length);
            this.ProcessBytes(bytes);
        }

        /// <summary>Read URLs from Sonar assets server</summary>
        [SuppressMessage("Minor Code Smell", "S1075", Justification = "Well known url")]
        [SuppressMessage("Security", "CA5394", Justification = "Not applicable.")] // Used for Task.Delay variation
        [SuppressMessage("Design", "CA1031", Justification = "Retry loop must survive any transport failure.")]
        private async Task BootstrapWebTask(CancellationToken cancellationToken)
        {
            var failures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                using var httpClient = HappyHttpUtils.CreateRandomlyHappyClient(timeout: BootstrapTimeout);
                try
                {
                    var response = await httpClient.GetAsync("https://assets.ffxivsonar.com/bootstrap/Urls.data", cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    this.ProcessBytes(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
                    if (failures > 0) this._logger.LogWarning(() => $"Bootstrap url refresh recovered after {failures} consecutive failures");
                    failures = 0;
                    await Task.Delay(TimeSpan.FromHours(6 * (SonarStatic.Random.NextDouble() + 0.5)), cancellationToken).ConfigureAwait(false);
                }
                // Only our own token ends the loop. A request timeout surfaces as the very same
                // exception type and has to reach the retry path below instead of returning.
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex)
                {
                    // One line per outage, not per retry.
                    if (failures++ == 0) this._logger.LogWarning(ex, () => "Bootstrap url refresh failed, retrying later (already known urls stay in use)");
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(6 * (SonarStatic.Random.NextDouble() + 0.5)), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                }
            }
        }

        private void ProcessBytes(byte[] bytes)
        {
            var urls = DeserializeUrls(bytes);
            foreach (var url in urls.Where(u => u.Debug == SonarConstants.DebugBuild))
            {
                this._urls.Remove(url); 
                if (url.Enabled) this._urls.Add(url);
            }
            this._urlArray = null;
        }

        public void Dispose()
        {
            this._cts.Cancel();
            this._cts.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await this._cts.CancelAsync().ConfigureAwait(false);
            await this._bootstrapTask.ConfigureAwait(false);
            this._cts.Dispose();
            GC.SuppressFinalize(this);
        }

        ~SonarUrlManager() => this.Dispose();
    }
}
