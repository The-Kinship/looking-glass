using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace LookingGlass;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly string[] CommandNames = { "/lookingglass", "/lglass" };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;
    internal static ModLibrary Library { get; private set; } = null!;

    private readonly WindowSystem windows = new("LookingGlass");
    private readonly MainWindow window;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Library = new ModLibrary();

        window = new MainWindow();
        windows.AddWindow(window);

        foreach (var cmd in CommandNames)
            CommandManager.AddHandler(cmd, new CommandInfo(OnCommand) { HelpMessage = "Open or close Rafter Cat's Looking Glass." });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
    }

    private void OnCommand(string command, string args) => Toggle();

    private void Toggle() => window.Toggle();

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        foreach (var cmd in CommandNames) CommandManager.RemoveHandler(cmd);
        windows.RemoveAllWindows();
    }
}
