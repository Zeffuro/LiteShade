using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LiteShade.Commands;
using LiteShade.Configuration.Persistence;
using LiteShade.Graphics;
using LiteShade.Profiles;
using LiteShade.Helpers;
using LiteShade.Windows;

namespace LiteShade;

public sealed class Plugin : IAsyncDalamudPlugin
{
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    private bool _configLoaded;
    private bool _disposed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginState.Reset();
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ConfigBackup.DoConfigBackup(PluginInterface);

        PluginState.Config = ConfigRepository.LoadOrDefault();
        _configLoaded = true;
        PluginState.ProfileService = new ProfileService(PluginState.Config);

        await IFramework.Get().RunOnFrameworkThread(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            PluginState.ColorFilter = new ColorFilter(PluginState.ProfileService);
            PluginState.DepthOfField = new DepthOfField(PluginState.ProfileService);
            PluginState.Vignette = new Vignette(PluginState.ProfileService);
        });

        PluginState.WindowSystem = new WindowSystem("LiteShade");
        PluginState.ConfigWindow = new ConfigWindow(PluginState.Config, PluginState.ProfileService, PluginState.ColorFilter!);
        PluginState.WindowSystem.AddWindow(PluginState.ConfigWindow);
        PluginState.WelcomeWindow = new WelcomeWindow(PluginState.Config, PluginState.ProfileService);
        PluginState.WindowSystem.AddWindow(PluginState.WelcomeWindow);

        if (!PluginState.Config.HasSeenWelcome)
        {
            PluginState.WelcomeWindow.Show();
        }

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.OpenMainUi += ToggleUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleUi;

        PluginState.CommandHandler = new CommandHandler();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        PluginState.CommandHandler?.Dispose();

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;

        PluginState.WindowSystem?.RemoveAllWindows();

        await IFramework.Get().RunOnFrameworkThread(() =>
        {
            try
            {
                PluginState.Vignette?.Dispose();
                PluginState.DepthOfField?.Dispose();
                PluginState.ColorFilter?.Dispose();
            }
            finally
            {
                PluginState.ProfileService?.Dispose();
            }
        });
        if (_configLoaded)
        {
            ConfigRepository.SaveImmediate(PluginState.Config);
        }

        PluginState.Reset();
    }

    private static void DrawUi() => PluginState.WindowSystem?.Draw();
    private static void ToggleUi() => PluginState.ConfigWindow?.Toggle();
}
