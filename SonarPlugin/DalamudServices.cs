using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace SonarPlugin
{
    // NOTE: IDalamudPluginInterface.GetRequiredService<T>() doesn't exist in TC's Dalamud
    // (it's a newer convenience method). This class replicates the same effect using the
    // older [PluginService]-attribute + IDalamudPluginInterface.Create<T>() idiom instead.
    internal sealed class DalamudServices
    {
        [PluginService] public IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] public IFramework Framework { get; private set; } = null!;
        [PluginService] public ICondition Condition { get; private set; } = null!;
        [PluginService] public IClientState ClientState { get; private set; } = null!;
        [PluginService] public IGameGui GameGui { get; private set; } = null!;
        [PluginService] public IChatGui ChatGui { get; private set; } = null!;
        [PluginService] public ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public IFateTable FateTable { get; private set; } = null!;
        [PluginService] public IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] public ISigScanner SigScanner { get; private set; } = null!;
        [PluginService] public IDataManager DataManager { get; private set; } = null!;
        [PluginService] public ITextureProvider TextureProvider { get; private set; } = null!;
    }
}
