using Dalamud.Interface.Windowing;
using LiteShade.Commands;
using LiteShade.Configuration;
using LiteShade.Graphics;
using LiteShade.Profiles;
using LiteShade.Windows;

namespace LiteShade;

internal static class PluginState
{
    public static SystemConfiguration Config { get; set; } = null!;
    public static WindowSystem WindowSystem { get; set; } = null!;
    public static ConfigWindow ConfigWindow { get; set; } = null!;
    public static WelcomeWindow WelcomeWindow { get; set; } = null!;
    public static ProfileService? ProfileService { get; set; }
    public static ColorFilter? ColorFilter { get; set; }
    public static DepthOfField? DepthOfField { get; set; }
    public static Vignette? Vignette { get; set; }
    public static CommandHandler? CommandHandler { get; set; }

    public static void Reset()
    {
        Config = null!;
        WindowSystem = null!;
        ConfigWindow = null!;
        WelcomeWindow = null!;
        ProfileService = null;
        ColorFilter = null;
        DepthOfField = null;
        Vignette = null;
        CommandHandler = null;
    }
}
