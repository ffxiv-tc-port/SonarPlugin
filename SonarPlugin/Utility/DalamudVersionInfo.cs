using Dalamud.Utility;

namespace SonarPlugin.Utility
{
    /// <summary>
    /// Minimal stand-in for Dalamud.Plugin.VersionInfo.IDalamudVersionInfo, which does not
    /// exist at this Dalamud API level (added upstream after TC's pinned API13). Sourced from
    /// Dalamud.Utility.Util's public static version members instead.
    /// </summary>
    public sealed class DalamudVersionInfo
    {
        public string Version { get; } = Util.AssemblyVersion;
        public string? GitHash { get; } = Util.GetGitHash();
    }
}
