using Dalamud.Bindings.ImGui;
using LiteShade.Configuration;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private void DrawOptions()
    {
        ImGui.TextUnformatted("Pause colours during:");
        PauseCheckbox("GPose", PauseOptions.GPose);
        PauseCheckbox("Cutscenes", PauseOptions.Cutscenes);
        PauseCheckbox("Idle camera", PauseOptions.IdleCamera);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The game's /icam camera. Use /icam <t> to focus on your target.");
        }

        PauseCheckbox("Portrait mode", PauseOptions.Portraits);
    }

    private void PauseCheckbox(string label, PauseOptions option)
    {
        var enabled = (_config.Pauses & option) != 0;
        if (ImGui.Checkbox(label, ref enabled))
        {
            _config.Pauses = enabled ? _config.Pauses | option : _config.Pauses & ~option;
            Save();
        }
    }
}
