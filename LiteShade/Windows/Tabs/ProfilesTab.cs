using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Configuration.Persistence;
using LiteShade.Graphics;
using LiteShade.Profiles;
using LiteShade.Windows.Components;

namespace LiteShade.Windows.Tabs;

internal sealed class ProfilesTab
{
    private readonly SystemConfiguration _config;
    private readonly ProfileService _profiles;
    private readonly Action _resetConditions;
    private readonly EffectEditor _effects;
    private readonly ProfileSharing _sharing;
    private readonly ProfileCopy _copy;
    private readonly GameFilterPicker _filters;
    private readonly PresetPicker _presets;
    private readonly Dictionary<Guid, ProfileHistory> _profileHistory = [];
    private bool _showOriginal;
    private bool _previewDirty;
    private bool _compareOnly;

    public Guid SelectedProfileId { get; set; }
    public bool IsPreviewing { get; private set; }
    public bool ShowingOriginal => _showOriginal;
    public bool HasPresetPreview => _presets.HasPreview;
    public SystemConfiguration Config => _config;
    public ProfileService Profiles => _profiles;
    public ColorFilter Filter { get; }
    public ProfilePicker Picker { get; }

    public ProfilesTab(SystemConfiguration config, ProfileService profiles, ColorFilter filter, Action resetConditions)
    {
        _config = config;
        _profiles = profiles;
        Filter = filter;
        _resetConditions = resetConditions;
        SelectedProfileId = config.DefaultProfileId;
        Picker = new ProfilePicker(config, Save);
        _filters = new GameFilterPicker(this);
        _presets = new PresetPicker(this);
        _effects = new EffectEditor(this, _filters);
        _sharing = new ProfileSharing(this);
        _copy = new ProfileCopy(this);
    }

    public void Draw()
    {
        if (ImGui.Button("Presets..."))
        {
            _presets.Open();
        }

        ImGui.SameLine();
        if (ImGui.Button("GPose filters..."))
        {
            ImGui.OpenPopup("GPose filters");
        }

        var selectedProfile = SelectedProfileId;
        if (Picker.Draw("Profile", ref selectedProfile))
        {
            StopPreview();
            _presets.StopPreview();
            SelectedProfileId = selectedProfile;
        }

        var profile = _config.Profiles.FirstOrDefault(item => item.Id == SelectedProfileId) ?? _config.GetDefaultProfile();
        SelectedProfileId = profile.Id;
        GetProfileHistory(profile);
        ImGui.SameLine();
        Picker.DrawFavourite(profile);
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
        _filters.DrawPicker(profile);
        DrawProfilePlayback(profile);

        ImGui.Separator();

        if (ImGui.Button("New"))
        {
            StopPreview();
            var created = new ColorProfile();
            _config.Profiles.Add(created);
            SelectedProfileId = created.Id;
            Save();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            StopPreview();
            var created = profile.Duplicate();
            _config.Profiles.Add(created);
            SelectedProfileId = created.Id;
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

        if (_sharing.Draw(profile) || _copy.Draw() || DrawDelete(profile))
        {
            return;
        }

        if (DrawProfileHistory(profile))
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

        var folder = profile.Folder;
        if (ImGui.InputTextWithHint("Folder", "Optional", ref folder, 81))
        {
            profile.Folder = folder;
        }

        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        ImGui.Spacing();
        var advanced = _config.ShowAdvancedControls;
        if (ImGui.Checkbox("Advanced controls", ref advanced))
        {
            _config.ShowAdvancedControls = advanced;
            ConfigRepository.Save(_config);
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hidden controls still apply.");
        if (_config.HiddenEffects != 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Some effects are hidden");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Choose which effects to show in Options. Hidden effects still apply.");
        }

        foreach (var effect in Effects.All)
        {
            if ((_config.HiddenEffects & effect) == 0) _effects.Draw(profile, effect);
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
            StopPreview();
            _config.Profiles.Remove(profile);
            _profileHistory.Remove(profile.Id);
            _config.FavoriteProfiles.Remove(profile.Id);
            foreach (var rule in _config.Rules.Where(rule => rule.ProfileId == profile.Id))
            {
                rule.Enabled = false;
            }

            _config.EnsureInitialized();
            SelectedProfileId = _config.DefaultProfileId;
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

    private void DrawProfilePlayback(ColorProfile profile)
    {
        var active = _config.Enabled && _profiles.Selection.ProfileId == profile.Id;
        var unavailable = _presets.HasPreview || PluginState.WelcomeWindow.IsOpen || PluginState.WelcomeWindow.HasPreview;
        using var disabled = ImRaii.Disabled(unavailable);
        if (IsPreviewing || !active)
        {
            if (ImGui.Button(IsPreviewing ? "End preview" : "Try profile"))
            {
                if (IsPreviewing) StopPreview();
                else
                {
                    _compareOnly = false;
                    IsPreviewing = true;
                    _showOriginal = false;
                    MarkPreviewDirty();
                }
            }
        }
        else ImGui.TextDisabled("Edits apply live");

        ImGui.SameLine();
        if (ImGui.Checkbox("Show original", ref _showOriginal))
        {
            if (_showOriginal && !IsPreviewing)
            {
                IsPreviewing = true;
                _compareOnly = true;
            }

            if (!_showOriginal && _compareOnly)
            {
                _compareOnly = false;
                StopPreview();
            }
            else MarkPreviewDirty();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(active))
        {
            if (ImGui.Button("Use now"))
            {
                StopPreview();
                if (!_config.Enabled)
                {
                    _config.Enabled = true;
                    Save();
                }

                _profiles.SetOverride(profile.Id);
            }
        }
    }

    private ProfileHistory GetProfileHistory(ColorProfile profile)
    {
        if (!_profileHistory.TryGetValue(profile.Id, out var history))
        {
            history = new ProfileHistory(profile);
            _profileHistory.Add(profile.Id, history);
        }

        return history;
    }

    private bool DrawProfileHistory(ColorProfile profile)
    {
        var history = GetProfileHistory(profile);
        ColorProfile? restored = null;
        using (ImRaii.Disabled(!history.CanUndo))
        {
            if (ImGui.Button("Undo"))
            {
                restored = history.Undo();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!history.CanRedo))
        {
            if (ImGui.Button("Redo"))
            {
                restored = history.Redo();
            }
        }

        if (restored is null)
        {
            return false;
        }

        _config.Profiles[_config.Profiles.FindIndex(item => item.Id == profile.Id)] = restored;
        Save();
        return true;
    }

    private void RecordProfileEdit()
    {
        var profile = _config.Profiles.FirstOrDefault(item => item.Id == SelectedProfileId);
        if (profile is not null)
        {
            GetProfileHistory(profile).Record(profile);
        }
    }

    public void Save()
    {
        ConfigRepository.Save(_config);
        RecordProfileEdit();
        _profiles.Refresh();
        MarkPreviewDirty();
    }

    public void StopPreview()
    {
        _profiles.BypassedEffects = 0;
        _compareOnly = false;
        if (!IsPreviewing)
        {
            return;
        }

        IsPreviewing = false;
        _showOriginal = false;
        _previewDirty = false;
        _profiles.SetPreview(null);
    }

    public void MarkPreviewDirty()
    {
        if (IsPreviewing)
        {
            _previewDirty = true;
        }
    }

    public bool ShowRendererStatus(ColorProfile profile)
        => !PluginState.WelcomeWindow.IsOpen && (IsPreviewing || profile.Id == _profiles.Selection.ProfileId);
    private void PublishEditorPreview(ColorProfile profile)
    {
        _profiles.SetPreview(_showOriginal ? ColorProfile.CreateNeutral() : profile);
        _previewDirty = false;
    }

    public void RefreshPreview()
    {
        if (_previewDirty)
        {
            PublishEditorPreview(_config.Profiles.First(profile => profile.Id == SelectedProfileId));
        }
    }

    public void DrawPresets() => _presets.Draw();

    public void DrawDialogs() => _sharing.DrawDialogs();

    public void FlushEdits() => _effects.FlushEdits();

    public void CloseDialogs()
    {
        _effects.FlushEdits(true);
        _sharing.CloseDialogs();
    }

    public void OpenCopyEffects(ColorProfile profile) => _copy.Open(profile);

    public void StopPresetPreview() => _presets.StopPreview();

    public void Imported(Guid profileId, bool replace)
    {
        StopPreview();
        StopPresetPreview();
        SelectedProfileId = profileId;
        _profileHistory.Clear();
        _resetConditions();
        if (replace)
        {
            PluginState.WelcomeWindow.IsOpen = false;
            _profiles.SetPreview(null);
            _profiles.SetOverride(null);
        }
    }

    public void Reset()
    {
        StopPreview();
        StopPresetPreview();
        _config.Reset();
        _profileHistory.Clear();
        SelectedProfileId = _config.DefaultProfileId;
        _resetConditions();
        _profiles.SetOverride(null);
        Save();
        PluginState.WelcomeWindow.Show();
    }
}
