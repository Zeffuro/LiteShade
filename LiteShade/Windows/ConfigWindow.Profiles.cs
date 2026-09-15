using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
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

        var defaultId = _config.DefaultProfileId;
        if (ProfileCombo("Default profile", ref defaultId))
        {
            _config.DefaultProfileId = defaultId;
            Save();
        }

        ImGui.Separator();

        if (ProfileCombo("Edit profile", ref _editingProfileId))
        {
            _confirmDelete = false;
        }

        var profile = _config.Profiles.FirstOrDefault(item => item.Id == _editingProfileId) ?? _config.GetDefaultProfile();
        _editingProfileId = profile.Id;
        if (ImGui.Button("New"))
        {
            var created = new ColorProfile();
            _config.Profiles.Add(created);
            _editingProfileId = created.Id;
            _confirmDelete = false;
            Save();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            var created = profile.Duplicate();
            _config.Profiles.Add(created);
            _editingProfileId = created.Id;
            _confirmDelete = false;
            Save();
            return;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(_config.Profiles.Count <= 1))
        {
            if (ImGui.Button("Delete"))
            {
                _confirmDelete = !_confirmDelete;
            }
        }

        if (DrawTransfer(profile))
        {
            return;
        }

        if (_confirmDelete)
        {
            ImGui.TextUnformatted("Delete this profile? Rules using it will be disabled.");
            if (ImGui.Button("Confirm delete"))
            {
                _config.Profiles.Remove(profile);
                foreach (var rule in _config.Rules.Where(rule => rule.ProfileId == profile.Id))
                {
                    rule.Enabled = false;
                }

                _config.EnsureInitialized();
                _editingProfileId = _config.DefaultProfileId;
                _confirmDelete = false;
                Save();
                return;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _confirmDelete = false;
            }
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
        Drag("Strength", profile.Strength, 0f, 1f, value => profile.Strength = value);
        Drag("Tint", profile.Tint, -1f, 1f, value => profile.Tint = value);
        Drag("Saturation", profile.Saturation, 0f, 2f, value => profile.Saturation = value);
        ImGui.Checkbox("Advanced", ref _advanced);
        if (_advanced)
        {
            Drag("Warmth", profile.Warmth, -1f, 1f, value => profile.Warmth = value);
            Drag("Contrast", profile.Contrast, 0.5f, 1.5f, value => profile.Contrast = value);
            Drag("Brightness", profile.Exposure, -2f, 2f, value => profile.Exposure = value);
        }

        if (ImGui.Button("Reset colours to neutral"))
        {
            profile.Strength = 1f;
            profile.Tint = 0f;
            profile.Warmth = 0f;
            profile.Saturation = 1f;
            profile.Contrast = 1f;
            profile.Exposure = 0f;
            Save();
        }

        ImGui.Separator();
        var depthOfField = profile.DepthOfField;
        if (ImGui.Checkbox("Depth of field", ref depthOfField))
        {
            profile.DepthOfField = depthOfField;
            Save();
        }

        if (depthOfField)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(PluginState.DepthOfField?.Status ?? "Unavailable");
            Drag("Aperture (f-number)", profile.FNumber, 0.7f, 32f, value => profile.FNumber = value,
                "Lower values blur more. Ctrl-click to type a value.");
            var manualFocus = !profile.AutoFocus;
            if (ImGui.Checkbox("Manual focus", ref manualFocus))
            {
                profile.AutoFocus = !manualFocus;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("When off, focus follows the camera's look-at point.");
            }

            if (manualFocus)
            {
                Drag("Focus distance", profile.FocusDistance, 0.5f, 200f, value => profile.FocusDistance = value,
                    "Distance from the camera. Ctrl-click to type a value.");
            }
            else
            {
                ImGui.TextDisabled("Focus: camera look-at point");
            }
        }
    }

    private void Drag(string label, float current, float min, float max, Action<float> set, string? tooltip = null)
    {
        var previous = current;
        if (ImGui.DragFloat(label, ref current, 0.01f, min, max, "%.2f"))
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
