using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Configuration.Persistence;
using LiteShade.Profiles;

namespace LiteShade.Windows.Tabs;

internal sealed class OptionsTab
{
    private readonly SystemConfiguration _config;
    private readonly ProfileService _profiles;
    private readonly Action _save;

    public OptionsTab(SystemConfiguration config, ProfileService profiles, Action save)
    {
        _config = config;
        _profiles = profiles;
        _save = save;
    }

    public void Draw()
    {
        if (ImGui.CollapsingHeader("Editor"))
        {
            var advanced = _config.ShowAdvancedControls;
            if (ImGui.Checkbox("Advanced controls", ref advanced))
            {
                _config.ShowAdvancedControls = advanced;
                ConfigRepository.Save(_config);
            }

            ImGui.TextUnformatted("Show effects:");
            foreach (var effect in Effects.All)
            {
                var visible = (_config.HiddenEffects & effect) == 0;
                if (ImGui.Checkbox(effect.Label(), ref visible))
                {
                    _config.HiddenEffects = visible ? _config.HiddenEffects & ~effect : _config.HiddenEffects | effect;
                    ConfigRepository.Save(_config);
                }
            }

            ImGui.TextDisabled("Hidden controls still apply.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Pause effects during:");
        using (var table = ImRaii.Table("Pause options", 6, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Effect", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("GPose", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Cutscenes", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Idle camera", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Portraits", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Combat", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableHeadersRow();
                foreach (var effect in Effects.All)
                {
                    DrawPauseRow(effect.Label(), _config.GetPauses(effect), pauses => _config.EffectPauses[effect] = pauses);
                }
            }
        }

        ImGui.Spacing();
        using var transitionTable = ImRaii.Table("Transition", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.PadOuterX);
        if (!transitionTable) return;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var transition = _config.TransitionSeconds;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Colour transition");
        ImGui.TableNextColumn();
        var resetSize = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - resetSize - ImGui.GetStyle().ItemSpacing.X));
        if (ImGui.SliderFloat("##Colour transition", ref transition, 0f, 3f, "%.2f s"))
        {
            _config.TransitionSeconds = float.IsFinite(transition) ? Math.Clamp(transition, 0f, 3f) : 0.35f;
            _profiles.Refresh();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Fades profile colours and colour pauses. GPose filters, depth of field and vignette change instantly. Ctrl-click to type a value.");
        }

        ImGui.SameLine();
        var reset = ImGuiComponents.IconButton("Reset transition", FontAwesomeIcon.Undo, new Vector2(resetSize / ImGuiHelpers.GlobalScale));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset");
        if (reset)
        {
            _config.TransitionSeconds = 0.35f;
            _profiles.Refresh();
            _save();
        }
    }

    private void DrawPauseRow(string label, PauseOptions pauses, Action<PauseOptions> set)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
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
        PauseCheckbox(PauseOptions.Combat, pauses, set);
    }

    private void PauseCheckbox(PauseOptions option, PauseOptions pauses, Action<PauseOptions> set)
    {
        ImGui.TableNextColumn();
        var enabled = (pauses & option) != 0;
        if (ImGui.Checkbox($"##{option}", ref enabled))
        {
            set(enabled ? pauses | option : pauses & ~option);
            _save();
        }
    }
}
