using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private void DrawOptions()
    {
        ImGui.TextUnformatted("Pause effects during:");
        using (var table = ImRaii.Table("Pause options", 5, ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Effect", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("GPose", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Cutscenes", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Idle camera", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Portrait mode", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                DrawPauseRow("Colour adjustments", _config.ColorPauses, pauses => _config.ColorPauses = pauses);
                DrawPauseRow("Depth of field", _config.DepthOfFieldPauses, pauses => _config.DepthOfFieldPauses = pauses);
                DrawPauseRow("Vignette", _config.VignettePauses, pauses => _config.VignettePauses = pauses);
            }
        }

        ImGui.Spacing();
        var transition = _config.TransitionSeconds;
        if (ImGui.SliderFloat("Profile transition (colours)", ref transition, 0f, 3f, "%.2f s"))
        {
            _config.TransitionSeconds = float.IsFinite(transition) ? Math.Clamp(transition, 0f, 3f) : 0.35f;
            _profiles.Refresh();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Native game filter changes are instant.");
        }
    }

    private void DrawPauseRow(string label, PauseOptions pauses, Action<PauseOptions> set)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        using var id = ImRaii.PushId(label);
        PauseCheckbox(PauseOptions.GPose, pauses, set);
        PauseCheckbox(PauseOptions.Cutscenes, pauses, set);
        PauseCheckbox(PauseOptions.IdleCamera, pauses, set);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The game's /icam camera. Use /icam <t> to focus on your target.");
        }

        PauseCheckbox(PauseOptions.Portraits, pauses, set);
    }

    private void PauseCheckbox(PauseOptions option, PauseOptions pauses, Action<PauseOptions> set)
    {
        ImGui.TableNextColumn();
        var enabled = (pauses & option) != 0;
        if (ImGui.Checkbox($"##{option}", ref enabled))
        {
            set(enabled ? pauses | option : pauses & ~option);
            Save();
        }
    }
}
