using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LiteShade.Configuration;
using LiteShade.Configuration.Persistence;
using LiteShade.Graphics;
using LiteShade.Integrations;
using LiteShade.Profiles;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows;

internal sealed class ConfigWindow : Window
{
    private readonly SystemConfiguration _config;
    private readonly ProfileService _profiles;
    private readonly ContextNames _names = new();
    private readonly ProfilesTab _profilesTab;
    private readonly ConditionsTab _conditionsTab;
    private readonly OptionsTab _optionsTab;
#if DEBUG
    private readonly DiagnosticsTab _diagnosticsTab;
#endif
    private readonly bool _reShadeLoaded;

    public ConfigWindow(SystemConfiguration config, ProfileService profiles, ColorFilter filter) : base("LiteShade")
    {
        _config = config;
        _profiles = profiles;
        _profilesTab = new ProfilesTab(config, profiles, filter, ResetConditions);
        _conditionsTab = new ConditionsTab(config, profiles, _names, _profilesTab.Picker, () => _profilesTab.SelectedProfileId, Save);
        _optionsTab = new OptionsTab(config, profiles, Save);
#if DEBUG
        _diagnosticsTab = new DiagnosticsTab(profiles, filter, _names);
#endif
        _reShadeLoaded = ReShadeDetector.IsLoaded();
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(600f, 520f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        _profilesTab.DrawDialogs();
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("Enable", ref enabled))
        {
            _config.Enabled = enabled;
            Save();
        }

        if (ConfigRepository.SaveError is { } error)
        {
            ImGui.TextWrapped(error);
            if (ImGui.SmallButton("Retry save"))
            {
                Save();
            }
        }

#if DEBUG
        var reShadeLoaded = _diagnosticsTab.ReShadeLoaded;
#else
        var reShadeLoaded = _reShadeLoaded;
#endif
        if (reShadeLoaded)
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationTriangle.ToIconString());
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("ReShade is loaded, its effects may overlap.");
            }
        }

        _profilesTab.DrawPresets();
        ImGui.Separator();
        var selection = _profiles.Selection;
        var selected = _config.Profiles.FirstOrDefault(profile => profile.Id == selection.ProfileId);
        var editing = _config.Profiles.FirstOrDefault(profile => profile.Id == _profilesTab.SelectedProfileId);
        ImGui.TextUnformatted(_profilesTab.HasPresetPreview ? "Trying a preset" : _profilesTab.IsPreviewing
            ? _profilesTab.ShowingOriginal ? "Previewing: original" : $"Previewing: {editing?.Name ?? "None"}"
            : $"Active profile: {selected?.Name ?? "None"}");
        if (!_profilesTab.IsPreviewing && !_profilesTab.HasPresetPreview)
        {
            var rule = _config.Rules.FirstOrDefault(item => item.Id == selection.RuleId);
            ImGui.SameLine();
            ImGui.TextDisabled(_profiles.OverrideProfileId.HasValue ? "override" : rule is null ? "default" : rule.Name);
            if (_profiles.OverrideProfileId.HasValue)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear override"))
                {
                    _profiles.SetOverride(null);
                }
            }
        }

        if (_profilesTab.SelectedProfileId != selection.ProfileId)
        {
            ImGui.TextDisabled($"Editing profile: {editing?.Name ?? "None"}");
        }
        using var tabs = ImRaii.TabBar("LiteShadeTabs");
        if (!tabs)
        {
            return;
        }

        using (var tab = ImRaii.TabItem("Profiles"))
        {
            if (tab)
            {
                _profilesTab.Draw();
            }
        }

        using (var tab = ImRaii.TabItem("Conditions"))
        {
            if (tab)
            {
                _conditionsTab.Draw();
            }
        }

        using (var tab = ImRaii.TabItem("Options"))
        {
            if (tab)
            {
                _optionsTab.Draw();
            }
        }

#if DEBUG
        using (var tab = ImRaii.TabItem("Diagnostics"))
        {
            if (tab)
            {
                _diagnosticsTab.Draw();
            }
        }
#endif
        _profilesTab.RefreshPreview();
    }

    private void Save() => _profilesTab.Save();

    private void ResetConditions() => _conditionsTab.ResetSelection();

    public void StopPreview() => _profilesTab.StopPreview();

    public void StopPresetPreview() => _profilesTab.StopPresetPreview();

    public override void OnClose()
    {
        _profilesTab.CloseDialogs();
        StopPreview();
        StopPresetPreview();
    }
}
