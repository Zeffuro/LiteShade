using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class EffectEditor
{
    private readonly ProfilesTab _editor;
    private readonly GameFilterPicker _filters;

    public EffectEditor(ProfilesTab editor, GameFilterPicker filters)
    {
        _editor = editor;
        _filters = filters;
    }

    public void Draw(ColorProfile profile, Effect effect)
    {
        using var id = ImRaii.PushId(effect.ToString());
        if (!ImGui.CollapsingHeader(effect.Label(), effect == Effect.ColourAdjustments ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            return;
        }

        if (effect is Effect.DepthOfField or Effect.Vignette)
        {
            var enabled = effect == Effect.DepthOfField ? profile.DepthOfField : profile.Vignette;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                if (effect == Effect.DepthOfField) profile.DepthOfField = enabled;
                else profile.Vignette = enabled;
                _editor.Save();
            }

            ImGui.SameLine();
        }

        var bypass = (_editor.Profiles.BypassedEffects & effect) != 0;
        using (ImRaii.Disabled(!_editor.ShowRendererStatus(profile)))
        {
            if (ImGui.Checkbox("Bypass", ref bypass))
            {
                _editor.Profiles.BypassedEffects = bypass ? _editor.Profiles.BypassedEffects | effect : _editor.Profiles.BypassedEffects & ~effect;
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(_editor.ShowRendererStatus(profile) ? "Temporary, clears when you close or switch profiles." : "Try this profile first.");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Reset"))
        {
            ResetEffect(profile, effect);
            _editor.Save();
        }

        if (_editor.ShowRendererStatus(profile))
        {
            ImGui.SameLine();
            var status = effect switch
            {
                Effect.DepthOfField => PluginState.DepthOfField?.Status ?? "Unavailable",
                Effect.Vignette => PluginState.Vignette?.Status ?? "Unavailable",
                _ => _editor.Filter.StatusFor(effect),
            };
            ImGui.TextDisabled(bypass ? "Bypassed" : status);
        }

        using var disabled = ImRaii.Disabled(bypass);
        switch (effect)
        {
            case Effect.ColourAdjustments: DrawColourControls(profile); break;
            case Effect.ShadowHighlight: DrawSplitTone(profile); break;
            case Effect.GPoseFilter:
                using (var table = ImRaii.Table("Filter control", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    if (table)
                    {
                        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
                        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
                        _filters.DrawControl(profile);
                    }
                }
                break;
            case Effect.DepthOfField:
                ImGui.TextDisabled("Experimental");
                using (ImRaii.Disabled(!profile.DepthOfField)) DrawDepthOfFieldControls(profile);
                break;
            case Effect.Vignette:
                using (ImRaii.Disabled(!profile.Vignette)) DrawVignetteControls(profile);
                break;
        }
    }

    private static void ResetEffect(ColorProfile profile, Effect effect)
    {
        switch (effect)
        {
            case Effect.ColourAdjustments:
                profile.Strength = profile.Saturation = profile.Contrast = 1;
                profile.Tint = profile.Warmth = profile.Exposure = 0;
                ResetLevels(profile);
                ResetMixer(profile);
                break;
            case Effect.ShadowHighlight:
                profile.ShadowColor = profile.HighlightColor = 0xFFFFFFFF;
                profile.ShadowStrength = profile.HighlightStrength = 0;
                break;
            case Effect.GPoseFilter: profile.GameFilterId = 0; break;
            case Effect.DepthOfField:
                profile.DepthOfField = false;
                profile.Focus = FocusMode.Target;
                profile.FocusDistance = 5;
                profile.FNumber = 4;
                break;
            case Effect.Vignette:
                profile.Vignette = false;
                profile.VignetteAmount = 0.35f;
                profile.VignetteRadius = 0.6f;
                profile.VignetteShape = 0.5f;
                profile.VignetteColor = 0xFF000000;
                break;
        }
    }

    private void DrawColourControls(ColorProfile profile)
    {
        using (var table = ImRaii.Table("Colour controls", 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
                Slider("Adjustment strength", profile.Strength, 0f, 1f, value => profile.Strength = value);
                Slider("Tint", profile.Tint, -1f, 1f, value => profile.Tint = value, "Green to magenta balance.");
                Slider("Temperature", profile.Warmth, -1f, 1f, value => profile.Warmth = value, "Cool to warm balance.");
                Slider("Saturation", profile.Saturation, 0f, 2f, value => profile.Saturation = value);
            }
        }

        using (var tone = ImRaii.Table("Tone controls", 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (tone)
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
                Slider("Contrast", profile.Contrast, 0.5f, 1.5f, value => profile.Contrast = value);
                Slider("Brightness", profile.Exposure, -2f, 2f, value => profile.Exposure = value);
            }
        }

        DrawLevels(profile);
        DrawChannelMixer(profile);
    }

    private void DrawLevels(ColorProfile profile)
    {
        using var node = ImRaii.TreeNode("Black/white levels");
        if (!node) return;

        if (ImGui.SmallButton("Reset"))
        {
            ResetLevels(profile);
            _editor.Save();
        }

        using var table = ImRaii.Table("Levels", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        Slider("Input black", profile.BlackLevel, 0f, Math.Min(0.95f, profile.WhiteLevel - 0.05f),
            value => profile.BlackLevel = value, "This input level becomes black.");
        Slider("Input white", profile.WhiteLevel, profile.BlackLevel + 0.05f, 1f,
            value => profile.WhiteLevel = value, "This input level becomes white.");
        Slider("Output black", profile.OutputBlackLevel, 0f, Math.Min(0.95f, profile.OutputWhiteLevel - 0.05f),
            value => profile.OutputBlackLevel = value, "Raises the darkest output level.");
        Slider("Output white", profile.OutputWhiteLevel, profile.OutputBlackLevel + 0.05f, 1f,
            value => profile.OutputWhiteLevel = value, "Lowers the brightest output level.");
    }

    private void DrawChannelMixer(ColorProfile profile)
    {
        using var node = ImRaii.TreeNode("RGB channel mixer");
        if (!node) return;

        if (ImGui.SmallButton("Reset"))
        {
            ResetMixer(profile);
            _editor.Save();
        }

        using var table = ImRaii.Table("Mixer", 4, ImGuiTableFlags.SizingStretchSame);
        if (!table) return;

        ImGui.TableSetupColumn("Output", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Red input", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Green input", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Blue input", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableHeadersRow();
        MixerRow("Red", profile.RedChannel, value => profile.RedChannel = value);
        MixerRow("Green", profile.GreenChannel, value => profile.GreenChannel = value);
        MixerRow("Blue", profile.BlueChannel, value => profile.BlueChannel = value);
    }

    private void MixerRow(string label, Vector3 current, Action<Vector3> set)
    {
        using var id = ImRaii.PushId(label);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        for (var input = 0; input < 3; input++)
        {
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var previous = current[input];
            var value = previous;
            if (ImGui.SliderFloat($"##{input}", ref value, -2f, 2f, "%.2f"))
            {
                current[input] = float.IsFinite(value) ? Math.Clamp(value, -2f, 2f) : previous;
                set(current);
                _editor.Profiles.Refresh();
                _editor.MarkPreviewDirty();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Contribution to the output channel. Ctrl-click to type a value.");
            if (ImGui.IsItemDeactivatedAfterEdit()) _editor.Save();
        }
    }

    private static void ResetLevels(ColorProfile profile)
    {
        profile.BlackLevel = profile.OutputBlackLevel = 0;
        profile.WhiteLevel = profile.OutputWhiteLevel = 1;
    }

    private static void ResetMixer(ColorProfile profile)
    {
        profile.RedChannel = Vector3.UnitX;
        profile.GreenChannel = Vector3.UnitY;
        profile.BlueChannel = Vector3.UnitZ;
    }

    private void DrawSplitTone(ColorProfile profile)
    {
        using var table = ImRaii.Table("Split tone", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        TintColour("Shadows", profile.ShadowColor, value => profile.ShadowColor = value);
        Slider("Shadow strength", profile.ShadowStrength, 0, 1, value => profile.ShadowStrength = value);
        TintColour("Highlights", profile.HighlightColor, value => profile.HighlightColor = value);
        Slider("Highlight strength", profile.HighlightStrength, 0, 1, value => profile.HighlightStrength = value);
    }

    private void TintColour(string label, uint packed, Action<uint> set)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        var colour = ImGui.ColorConvertU32ToFloat4(packed);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.ColorEdit4($"##{label}", ref colour, ImGuiColorEditFlags.NoAlpha))
        {
            set(ImGui.ColorConvertFloat4ToU32(colour));
            _editor.Profiles.Refresh();
            _editor.MarkPreviewDirty();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _editor.Save();
        }
    }

    private void DrawVignetteControls(ColorProfile profile)
    {
        using var table = ImRaii.Table("Vignette controls", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        Slider("Amount", profile.VignetteAmount, 0f, 1f, value => profile.VignetteAmount = value, "Corner opacity");
        Slider("Radius", profile.VignetteRadius, 0f, 0.95f, value => profile.VignetteRadius = value, "Centre clear area");
        Slider("Shape", profile.VignetteShape, 0f, 1f, value => profile.VignetteShape = value, "Circle to ellipse");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Colour");
        ImGui.TableNextColumn();
        var colour = ImGui.ColorConvertU32ToFloat4(profile.VignetteColor);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.ColorEdit4("##Colour", ref colour, ImGuiColorEditFlags.NoAlpha))
        {
            profile.VignetteColor = ImGui.ColorConvertFloat4ToU32(colour);
            _editor.Profiles.Refresh();
            _editor.MarkPreviewDirty();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _editor.Save();
        }
    }

    private void DrawDepthOfFieldControls(ColorProfile profile)
    {
        using var table = ImRaii.Table("Depth of field controls", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        Slider("Aperture", profile.FNumber, 0.5f, 32f, value => profile.FNumber = value,
            "Lower values blur more. Ctrl-click to type a value.", ImGuiSliderFlags.Logarithmic);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Focus");
        ImGui.TableNextColumn();
        var focus = profile.Focus;
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##Focus", FocusLabel(focus)))
        {
            if (combo)
            {
                foreach (var mode in Enum.GetValues<FocusMode>())
                {
                    if (ImGui.Selectable(FocusLabel(mode), mode == focus))
                    {
                        focus = mode;
                        profile.Focus = focus;
                        _editor.Save();
                    }
                }
            }
        }

        if (focus == FocusMode.Manual)
        {
            Drag("Fixed distance", profile.FocusDistance, 0.5f, 200f, value => profile.FocusDistance = value,
                "Distance from the camera. Ctrl-click to type a value.");
        }

        var depthOfField = _editor.ShowRendererStatus(profile) ? PluginState.DepthOfField : null;
        var description = depthOfField?.FocusDescription;
        if (!string.IsNullOrEmpty(description))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Effective focus");
            ImGui.TableNextColumn();
            var distance = depthOfField!.EffectiveFocusDistance;
            ImGui.TextDisabled(float.IsFinite(distance) ? $"{description} · {distance:F2} yalms" : description);
        }
    }

    private void Slider(string label, float current, float min, float max, Action<float> set, string? tooltip = null,
        ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        var previous = current;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat($"##{label}", ref current, min, max, "%.2f", flags))
        {
            set(float.IsFinite(current) ? Math.Clamp(current, min, max) : previous);
            _editor.Profiles.Refresh();
            _editor.MarkPreviewDirty();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip ?? "Ctrl-click to type a value");
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _editor.Save();
        }
    }

    private void Drag(string label, float current, float min, float max, Action<float> set, string? tooltip = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        var previous = current;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.DragFloat($"##{label}", ref current, 0.01f, min, max, "%.2f"))
        {
            set(float.IsFinite(current) ? Math.Clamp(current, min, max) : previous);
            _editor.Profiles.Refresh();
            _editor.MarkPreviewDirty();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip ?? "Ctrl-click to type a value");
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _editor.Save();
        }
    }

    private static string FocusLabel(FocusMode focus) => focus switch
    {
        FocusMode.Target => "Selected target",
        FocusMode.Camera => "Camera focus point",
        FocusMode.Manual => "Fixed distance",
        _ => "Selected target",
    };
}
