using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private void DrawProfiles()
    {
        if (ImGui.Button("Presets..."))
        {
            PluginState.WelcomeWindow.Show();
        }

        ProfileCombo("Profile", ref _editingProfileId);
        var profile = _config.Profiles.FirstOrDefault(item => item.Id == _editingProfileId) ?? _config.GetDefaultProfile();
        _editingProfileId = profile.Id;
        ImGui.SameLine();
        using (ImRaii.Disabled(profile.Id == _config.DefaultProfileId))
        {
            if (ImGui.Button("Set as default"))
            {
                _config.DefaultProfileId = profile.Id;
                Save();
            }
        }

        ImGui.TextDisabled($"Default: {_config.GetDefaultProfile().Name}");
        ImGui.Separator();

        if (ImGui.Button("New"))
        {
            var created = new ColorProfile();
            _config.Profiles.Add(created);
            _editingProfileId = created.Id;
            Save();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            var created = profile.Duplicate();
            _config.Profiles.Add(created);
            _editingProfileId = created.Id;
            Save();
            return;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(_config.Profiles.Count <= 1))
        {
            if (ImGui.Button("Delete"))
            {
                ImGui.OpenPopup("Delete profile");
            }
        }

        if (DrawTransfer(profile) || DrawDelete(profile))
        {
            return;
        }

        var name = profile.Name;
        if (ImGui.InputText("Name", ref name, 81))
        {
            profile.Name = name;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Colour adjustments");
        if (profile.Id == _profiles.Selection.ProfileId)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_filter.Status);
        }

        DrawColourControls(profile);

        if (ImGui.Button("Reset colours to neutral"))
        {
            profile.Strength = 1f;
            profile.Tint = 0f;
            profile.Warmth = 0f;
            profile.Saturation = 1f;
            profile.Contrast = 1f;
            profile.Exposure = 0f;
            profile.GameFilterId = 0;
            Save();
        }

        ImGui.Separator();
        var depthOfField = profile.DepthOfField;
        if (ImGui.Checkbox("Depth of field", ref depthOfField))
        {
            profile.DepthOfField = depthOfField;
            Save();
        }

        if (profile.Id == _profiles.Selection.ProfileId)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(PluginState.DepthOfField?.Status ?? "Unavailable");
        }

        if (depthOfField)
        {
            DrawDepthOfFieldControls(profile);
        }

        ImGui.Separator();
        var vignette = profile.Vignette;
        if (ImGui.Checkbox("Vignette", ref vignette))
        {
            profile.Vignette = vignette;
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Overrides the game's vignette while enabled.");
        }

        if (profile.Id == _profiles.Selection.ProfileId)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(PluginState.Vignette?.Status ?? "Unavailable");
        }

        if (vignette)
        {
            DrawVignetteControls(profile);
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
                Slider("Colour strength", profile.Strength, 0f, 1f, value => profile.Strength = value);
                Slider("Tint", profile.Tint, -1f, 1f, value => profile.Tint = value);
                Slider("Temperature", profile.Warmth, -1f, 1f, value => profile.Warmth = value);
                Slider("Saturation", profile.Saturation, 0f, 2f, value => profile.Saturation = value);
                DrawGameFilter(profile);
            }
        }

        ImGui.TextUnformatted("Tone");
        using var tone = ImRaii.Table("Tone controls", 2, ImGuiTableFlags.SizingStretchProp);
        if (!tone)
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
        Slider("Contrast", profile.Contrast, 0.5f, 1.5f, value => profile.Contrast = value);
        Slider("Exposure", profile.Exposure, -2f, 2f, value => profile.Exposure = value);
    }

    private void DrawGameFilter(ColorProfile profile)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Game filter");
        ImGui.TableNextColumn();
        var selected = profile.GameFilterId;
        var preview = selected == 0 ? "None" : $"Unavailable ({selected})";
        foreach (var filter in _filter.GameFilters)
        {
            if (filter.Id == selected)
            {
                preview = $"{filter.Name} ({filter.Id})";
                break;
            }
        }

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##Game filter", preview);
        if (!combo)
        {
            return;
        }

        if (ImGui.Selectable("None", selected == 0))
        {
            profile.GameFilterId = 0;
            Save();
        }

        foreach (var filter in _filter.GameFilters)
        {
            if (ImGui.Selectable($"{filter.Name} ({filter.Id})", filter.Id == selected))
            {
                profile.GameFilterId = filter.Id;
                Save();
            }
        }

        if (_filter.GameFiltersError is { } error)
        {
            ImGui.TextDisabled(error);
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
            _profiles.Refresh();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
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
            "Lower values blur more. Ctrl-click to type a value.");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Focus");
        ImGui.TableNextColumn();
        var focus = profile.Focus;
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##Focus", focus.ToString()))
        {
            if (combo)
            {
                foreach (var mode in Enum.GetValues<FocusMode>())
                {
                    if (ImGui.Selectable(mode.ToString(), mode == focus))
                    {
                        focus = mode;
                        profile.Focus = focus;
                        Save();
                    }
                }
            }
        }

        if (focus == FocusMode.Manual)
        {
            Drag("Focus distance", profile.FocusDistance, 0.5f, 200f, value => profile.FocusDistance = value,
                "Distance from the camera. Ctrl-click to type a value.");
        }

        var description = profile.Id == _profiles.Selection.ProfileId
            ? PluginState.DepthOfField?.FocusDescription
            : null;
        if (!string.IsNullOrEmpty(description))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Focused on");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(description);
        }
    }

    private bool DrawDelete(ColorProfile profile)
    {
        var open = true;
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Delete profile", ref open, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            return false;
        }

        ImGui.TextWrapped($"Delete {profile.Name}? Rules using it will be disabled.");
        if (ImGui.Button("Delete profile"))
        {
            _config.Profiles.Remove(profile);
            foreach (var rule in _config.Rules.Where(rule => rule.ProfileId == profile.Id))
            {
                rule.Enabled = false;
            }

            _config.EnsureInitialized();
            _editingProfileId = _config.DefaultProfileId;
            Save();
            ImGui.CloseCurrentPopup();
            return true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
        }

        return false;
    }

    private void Slider(string label, float current, float min, float max, Action<float> set, string? tooltip = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        var previous = current;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat($"##{label}", ref current, min, max, "%.2f"))
        {
            set(float.IsFinite(current) ? Math.Clamp(current, min, max) : previous);
            _profiles.Refresh();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip ?? "Ctrl-click to type a value");
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
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
            _profiles.Refresh();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip ?? "Ctrl-click to type a value");
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
        }
    }
}
