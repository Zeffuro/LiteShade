using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using LiteShade.Helpers;
using LiteShade.Configuration.Persistence;

namespace LiteShade.Commands;

internal sealed class CommandHandler : IDisposable
{
    private const string Command = "/liteshade";

    public CommandHandler()
    {
        ICommandManager.Get().AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open LiteShade. Options: status, profile <name>, auto, toggle.",
        });
    }

    public void Dispose() => ICommandManager.Get().RemoveHandler(Command);

    private static void OnCommand(string command, string arguments)
    {
        var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            PluginState.ConfigWindow.Toggle();
            return;
        }

        var profiles = PluginState.ProfileService;
        switch (parts[0].ToLowerInvariant())
        {
            case "status":
                Print($"Colour: {PluginState.ColorFilter?.Status ?? "Unavailable"}. Depth of field: {PluginState.DepthOfField?.Status ?? "Unavailable"}. Vignette: {PluginState.Vignette?.Status ?? "Unavailable"}.");
                break;
            case "toggle":
                PluginState.Config.Enabled = !PluginState.Config.Enabled;
                ConfigRepository.Save(PluginState.Config);
                profiles?.Refresh();
                Print(PluginState.Config.Enabled ? "Enabled." : "Disabled.");
                break;
            case "auto":
                profiles?.SetOverride(null);
                Print("Profile override cleared.");
                break;
            case "profile" when parts.Length == 2:
                var name = parts[1].Trim();
                var matches = PluginState.Config.Profiles.Where(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                {
                    Print(matches.Length == 0 ? $"Profile not found: {name}" : $"More than one profile is named {name}. Rename one first.");
                    break;
                }

                if (profiles?.SetOverride(matches[0].Id) == true)
                {
                    Print($"Profile: {matches[0].Name}. Use /liteshade auto to clear the override."
                        + (PluginState.Config.Enabled ? string.Empty : " LiteShade is disabled."));
                }
                break;
            default:
                Print("Use /liteshade [status | profile <name> | auto | toggle].");
                break;
        }
    }

    private static void Print(string message) => IChatGui.Get().Print(message, "LiteShade");
}
