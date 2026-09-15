using System;
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

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow : Window
{
    private readonly SystemConfiguration _config;
    private readonly ProfileService _profiles;
    private readonly ColorFilter _filter;
    private readonly ContextNames _names = new();
    private bool _reShadeLoaded;
    private Guid _editingProfileId;
    private Guid? _editingRuleId;
    private bool _editorPreview;
    private bool _editorShowOriginal;
    private bool _editorPreviewDirty;

    public ConfigWindow(SystemConfiguration config, ProfileService profiles, ColorFilter filter) : base("LiteShade")
    {
        _config = config;
        _profiles = profiles;
        _filter = filter;
        _editingProfileId = config.DefaultProfileId;
        _reShadeLoaded = ReShadeDetector.IsLoaded();
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(600f, 520f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
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

        if (_reShadeLoaded)
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

        ImGui.Separator();
        var selection = _profiles.Selection;
        var selected = _config.Profiles.FirstOrDefault(profile => profile.Id == selection.ProfileId);
        var editing = _config.Profiles.FirstOrDefault(profile => profile.Id == _editingProfileId);
        ImGui.TextUnformatted(_editorPreview
            ? _editorShowOriginal ? "Previewing: original" : $"Previewing: {editing?.Name ?? "None"}"
            : $"Active profile: {selected?.Name ?? "None"}");
        if (!_editorPreview)
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

        if (_editingProfileId != selection.ProfileId)
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
                DrawProfiles();
            }
        }

        using (var tab = ImRaii.TabItem("Conditions"))
        {
            if (tab)
            {
                DrawConditionEditor();
            }
        }

        using (var tab = ImRaii.TabItem("Options"))
        {
            if (tab)
            {
                DrawOptions();
            }
        }

#if DEBUG
        using (var tab = ImRaii.TabItem("Diagnostics"))
        {
            if (tab)
            {
                DrawDiagnostics();
            }
        }
#endif
        if (_editorPreviewDirty)
        {
            PublishEditorPreview(_config.Profiles.First(profile => profile.Id == _editingProfileId));
        }
    }

    private void Save()
    {
        ConfigRepository.Save(_config);
        _profiles.Refresh();
        MarkPreviewDirty();
    }

    public void StopPreview()
    {
        if (!_editorPreview)
        {
            return;
        }

        _editorPreview = false;
        _editorShowOriginal = false;
        _editorPreviewDirty = false;
        _profiles.SetPreview(null);
    }

    public override void OnClose() => StopPreview();

    private void MarkPreviewDirty()
    {
        if (_editorPreview)
        {
            _editorPreviewDirty = true;
        }
    }

    private bool ProfileCombo(string label, ref Guid selected)
    {
        var selectedId = selected;
        var current = _config.Profiles.FirstOrDefault(profile => profile.Id == selectedId);
        using var combo = ImRaii.Combo(label, current?.Name ?? "Missing profile");
        if (!combo)
        {
            return false;
        }

        foreach (var profile in _config.Profiles)
        {
            using var id = ImRaii.PushId(profile.Id.ToString());
            if (ImGui.Selectable(profile.Name, profile.Id == selected))
            {
                selected = profile.Id;
                return true;
            }
        }

        return false;
    }
}
