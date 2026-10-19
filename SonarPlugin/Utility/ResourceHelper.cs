using Dalamud.Interface;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using DryIocAttributes;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace SonarPlugin.Utility
{
    [ExportMany]
    [SingletonReuse]
    public sealed class ResourceHelper
    {
        private ITextureProvider Textures { get; }
        private IPluginLog Logger { get; }

        public ResourceHelper(ITextureProvider textures, IPluginLog logger)
        {
            this.Textures = textures;
            this.Logger = logger;
        }

        /// <summary>Creates the tiny 1x1 placeholder icon. Synchronous and effectively instant
        /// (no decode, just a raw pixel buffer), safe to call inline anywhere.</summary>
        public IDalamudTextureWrap LoadFallbackIcon() => this.Textures.CreateFromRaw(new(1, 1, 28), [255, 0, 0, 127]);

        /// <summary>Loads an embedded icon image asynchronously. Callers must not block on the
        /// returned task (e.g. via .Result/.Wait()) from the main/framework thread; await it or
        /// background it instead.</summary>
        public async Task<IDalamudTextureWrap> LoadIconAsync(string filename)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"SonarPlugin.Resources.Icons.{filename}");
            if (stream is null)
            {
                this.Logger.Warning($"Embedded resource not found while loading icon image: {filename}");
                return this.LoadFallbackIcon();
            }

            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            try
            {
                return await this.Textures.CreateFromImageAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, $"Failed to load icon image: {filename}, loading fallback");
                return this.LoadFallbackIcon();
            }
        }
    }
}
