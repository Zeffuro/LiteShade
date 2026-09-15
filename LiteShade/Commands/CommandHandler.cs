using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using LiteShade.Helpers;

namespace LiteShade.Commands;

internal sealed class CommandHandler : IDisposable
{
    private const string Command = "/liteshade";

    public CommandHandler()
    {
        ICommandManager.Get().AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open LiteShade. Use /liteshade status for rendering availability.",
        });
    }

    public void Dispose() => ICommandManager.Get().RemoveHandler(Command);

    private static void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            IChatGui.Get().Print(PluginState.ColorFilter?.Status ?? "Colour rendering is unavailable.", "LiteShade");
            return;
        }

        PluginState.ConfigWindow.Toggle();
    }
}
