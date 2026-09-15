using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LiteShade.Configuration;
using LiteShade.Configuration.Persistence;
using LiteShade.Profiles;

namespace LiteShade.Windows;

internal sealed class WelcomeWindow : Window
{
    private readonly SystemConfiguration _config;
    private readonly ProfileService _profiles;
    private int _selectedPreset = -1;
    private ColorProfile? _preview;
    private bool _showOriginal;

    public WelcomeWindow(SystemConfiguration config, ProfileService profiles)
        : base("LiteShade presets###LiteShadeWelcome")
    {
        _config = config;
        _profiles = profiles;
        ShowCloseButton = true;
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(420f, 0f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Show()
    {
        _selectedPreset = -1;
        _preview = null;
        _showOriginal = false;
        _profiles.SetPreview(null);
        IsOpen = true;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Pick a starting look");
        ImGui.TextDisabled("Select one to preview it. You can change it later.");
        ImGui.Spacing();

        for (var index = 0; index < BuiltInPresets.All.Count; index++)
        {
            var preset = BuiltInPresets.All[index];
            using var id = ImRaii.PushId(index);
            if (ImGui.Selectable(preset.Name, index == _selectedPreset))
            {
                Select(index);
            }

            ImGui.TextDisabled(preset.Description);
        }

        if (_preview is not null)
        {
            ImGui.Spacing();
            if (ImGui.Checkbox("Show original", ref _showOriginal))
            {
                UpdatePreview();
            }

            var strength = _preview.Strength;
            var previous = strength;
            if (ImGui.SliderFloat("Colour strength", ref strength, 0f, 1f, "%.2f"))
            {
                _preview.Strength = float.IsFinite(strength) ? Math.Clamp(strength, 0f, 1f) : previous;
                UpdatePreview();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Ctrl-click to type a value");
            }
        }

        ImGui.Spacing();
        using (ImRaii.Disabled(_preview is null))
        {
            if (ImGui.Button("Use preset"))
            {
                Keep();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            Skip();
        }
    }

    public override void OnClose() => Skip();

    private void Select(int index)
    {
        _selectedPreset = index;
        _preview = BuiltInPresets.All[index].CreateProfile();
        _showOriginal = false;
        UpdatePreview();
    }

    private void UpdatePreview() => _profiles.SetPreview(_showOriginal ? ColorProfile.CreateNeutral() : _preview);

    private void Keep()
    {
        if (_preview is null)
        {
            return;
        }

        _preview.Name = UniqueName(_preview.Name);
        _config.Profiles.Add(_preview);
        _config.DefaultProfileId = _preview.Id;
        _config.Enabled = true;
        _config.AutomaticProfiles = false;
        _config.HasSeenWelcome = true;
        Save();
        _profiles.SetOverride(null);
        _profiles.SetPreview(null);
        IsOpen = false;
    }

    private void Skip()
    {
        _profiles.SetPreview(null);
        if (!_config.HasSeenWelcome)
        {
            _config.HasSeenWelcome = true;
            Save();
        }

        IsOpen = false;
    }

    private string UniqueName(string baseName)
    {
        if (_config.Profiles.All(profile => !string.Equals(profile.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var name = $"{baseName} {suffix}";
            if (_config.Profiles.All(profile => !string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private void Save()
    {
        ConfigRepository.Save(_config);
        _profiles.Refresh();
    }
}
