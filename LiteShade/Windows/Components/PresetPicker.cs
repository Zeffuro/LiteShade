using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class PresetPicker
{
    private readonly ProfilesTab _editor;
    private int _selectedPreset = -1;
    private ColorProfile? _presetPreview;
    private bool _presetShowOriginal;
    private bool _openPresetPicker;

    public PresetPicker(ProfilesTab editor) => _editor = editor;

    public bool HasPreview => _presetPreview is not null;

    public void Open()
    {
        _editor.StopPreview();
        StopPreview();
        _selectedPreset = -1;
        _openPresetPicker = true;
    }

    public void StopPreview()
    {
        _openPresetPicker = false;
        if (_presetPreview is null)
        {
            return;
        }

        _presetPreview = null;
        _presetShowOriginal = false;
        if (!_editor.IsPreviewing && !PluginState.WelcomeWindow.HasPreview)
        {
            _editor.Profiles.SetPreview(null);
        }
    }

    public void Draw()
    {
        if (_openPresetPicker)
        {
            ImGui.OpenPopup("Presets");
            _openPresetPicker = false;
        }

        using var popup = ImRaii.Popup("Presets", ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            StopPreview();
            return;
        }

        ImGui.TextUnformatted("Built-in presets");
        ImGui.TextDisabled("Select a look to preview it, then add a profile.");
        var unavailable = _editor.IsPreviewing || PluginState.WelcomeWindow.IsOpen || PluginState.WelcomeWindow.HasPreview;
        using (ImRaii.Disabled(unavailable))
        {
            if (ImGui.ArrowButton("Previous preset", ImGuiDir.Left))
            {
                CyclePreset(-1);
            }

            ImGui.SameLine();
            if (ImGui.ArrowButton("Next preset", ImGuiDir.Right))
            {
                CyclePreset(1);
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(_selectedPreset < 0 ? "Select a preset" : BuiltInPresets.All[_selectedPreset].Name);

            var appearing = ImGui.IsWindowAppearing();
            using (var child = ImRaii.Child("Preset list", ImGuiHelpers.ScaledVector2(320f, 150f)))
            {
                if (child)
                {
                    for (var index = 0; index < BuiltInPresets.All.Count; index++)
                    {
                        using var id = ImRaii.PushId(index);
                        if (ImGui.Selectable(BuiltInPresets.All[index].Name, index == _selectedPreset,
                                ImGuiSelectableFlags.DontClosePopups))
                        {
                            SelectPreset(index);
                        }

                        if (appearing && index == _selectedPreset)
                        {
                            ImGui.SetScrollHereY();
                        }
                    }
                }
            }

            if (_selectedPreset >= 0)
            {
                ImGui.TextDisabled(BuiltInPresets.All[_selectedPreset].Description);
            }

            using (ImRaii.Disabled(_presetPreview is null))
            {
                if (ImGui.Checkbox("Show original", ref _presetShowOriginal))
                {
                    PublishPresetPreview();
                }

                ImGui.Separator();
                if (ImGui.Button("Add profile"))
                {
                    AddPresetProfile();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            StopPreview();
            ImGui.CloseCurrentPopup();
        }
    }

    private void CyclePreset(int direction)
    {
        var count = BuiltInPresets.All.Count;
        var next = _selectedPreset < 0 ? direction > 0 ? 0 : count - 1 : (_selectedPreset + direction + count) % count;
        SelectPreset(next);
    }

    private void SelectPreset(int index)
    {
        _selectedPreset = index;
        _presetPreview = BuiltInPresets.All[index].CreateProfile();
        _presetShowOriginal = false;
        PublishPresetPreview();
    }

    private void PublishPresetPreview()
        => _editor.Profiles.SetPreview(_presetShowOriginal ? ColorProfile.CreateNeutral() : _presetPreview);

    private void AddPresetProfile()
    {
        if (_presetPreview is null)
        {
            return;
        }

        var profile = _presetPreview.Copy();
        profile.Id = Guid.NewGuid();
        profile.Name = UniquePresetName(profile.Name);
        _editor.Config.Profiles.Add(profile);
        _editor.SelectedProfileId = profile.Id;
        StopPreview();
        _editor.Save();
    }

    private string UniquePresetName(string name)
    {
        var baseName = ColorProfile.NormalizeName(name, "Unnamed profile");
        var candidate = baseName;
        for (var suffix = 2; _editor.Config.Profiles.Any(profile =>
                 string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            var ending = $" {suffix}";
            candidate = baseName[..Math.Min(baseName.Length, 80 - ending.Length)] + ending;
        }

        return candidate;
    }
}
