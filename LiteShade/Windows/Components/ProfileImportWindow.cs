using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Configuration.Sharing;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class ProfileImportWindow(ProfilesTab editor, Action<SharedSetupSnapshot> imported)
{
    private ProfileImport? _pending;
    private ProfileExport? _prepared;
    private bool _open;
    private bool _dirty;
    private bool _includeRules;
    private bool _enableRules;
    private bool _replace;
    private bool _update;
    private string? _error;
    private string _changes = string.Empty;

    private ImportMode Mode => _replace ? ImportMode.Replace : _update ? ImportMode.Update : ImportMode.Add;

    public void Open(string text)
    {
        _pending = new ProfileImport(ProfileTransfer.Parse(text));
        _includeRules = _pending.Source.Rules.Count > 0;
        _enableRules = _replace = false;
        _update = ProfileTransfer.GetDefaultMode(editor.Config, _pending.Source) == ImportMode.Update;
        _error = null;
        _open = _dirty = true;
    }

    public void Cancel() => _pending = null;

    public bool Draw()
    {
        if (_pending is null) return false;
        if (_open)
        {
            ImGui.OpenPopup("Import profiles");
            _open = false;
        }

        var open = true;
        var width = 620f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Import profiles", ref open, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            if (!open) Cancel();
            return false;
        }

        var source = _pending.Source;
        var installed = ProfileTransfer.GetInstalledPack(editor.Config, source);
        if (source.Pack is { } pack)
        {
            ImGui.TextUnformatted($"{pack.Name} · revision {pack.Revision}");
            if (pack.Author.Length > 0) ImGui.TextDisabled($"By {pack.Author}");
            if (installed is not null) ImGui.TextDisabled($"Installed revision: {installed.Pack.Revision}");
        }

        if (source.MinimumPluginVersion is { } version) ImGui.TextDisabled($"Requires LiteShade {version}");
        DrawSelection();
        ImGui.Spacing();
        if (installed is not null)
        {
            using var disabled = ImRaii.Disabled(_replace);
            if (ImGui.Checkbox("Update installed pack", ref _update)) _dirty = true;
        }

        if (ImGui.Checkbox("Replace all profiles and conditions", ref _replace)) _dirty = true;
        if (_replace)
        {
            Warning("Your existing profiles and conditions will be replaced.");
            ImGui.TextDisabled($"Default profile: {_pending.DefaultProfileName}");
        }
        else if (_update)
        {
            Warning("Local edits to selected pack profiles and conditions will be replaced.");
            if (installed is not null && source.Pack?.Revision <= installed.Pack.Revision)
                ImGui.TextDisabled("This is the same revision or an older one.");
        }

        if (source.Rules.Count > 0 || installed?.RuleIds.Count > 0)
        {
            if (ImGui.Checkbox("Include conditions", ref _includeRules)) _dirty = true;
            using (ImRaii.Disabled(!_includeRules || _pending.RuleCount == 0))
            {
                if (ImGui.Checkbox("Enable imported conditions", ref _enableRules)) _dirty = true;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("New conditions stay disabled unless checked. Updates keep existing enabled states.");
        }

        Prepare();
        if (_error is not null) ImGui.TextColored(ImGuiColors.DalamudRed, _error);
        else ImGui.TextWrapped(_changes);
        using (ImRaii.Disabled(_prepared is null))
        {
            if (ImGui.Button(_replace ? "Replace and import" : _update ? "Update pack" : "Import"))
            {
                var backup = SharedSetupSnapshot.Capture(editor.Config);
                try
                {
                    var id = ProfileTransfer.Import(editor.Config, _prepared!, _includeRules, _enableRules, Mode);
                    imported(backup);
                    editor.Imported(id, Mode != ImportMode.Add);
                    editor.Save();
                    Cancel();
                    ImGui.CloseCurrentPopup();
                    return true;
                }
                catch (FormatException exception)
                {
                    _error = exception.Message;
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            Cancel();
            ImGui.CloseCurrentPopup();
        }

        return false;
    }

    private void Prepare()
    {
        if (!_dirty || _pending is null) return;
        _dirty = false;
        _prepared = null;
        _error = null;
        _changes = string.Empty;
        if (_pending.Selected.Count == 0) return;
        try
        {
            var selected = _pending.Prepare(_includeRules);
            var changes = ProfileTransfer.GetChanges(editor.Config, selected, _includeRules, Mode);
            _changes = $"Profiles: {changes.ProfilesAdded} new, {changes.ProfilesUpdated} updated";
            if (_includeRules)
                _changes += $"\nConditions: {changes.RulesAdded} new, {changes.RulesUpdated} updated, {changes.RulesRemoved} removed";
            _prepared = selected;
        }
        catch (FormatException exception)
        {
            _error = exception.Message;
        }
    }

    private void DrawSelection()
    {
        var import = _pending!;
        ImGui.TextDisabled($"{import.Selected.Count} / {import.Source.Profiles.Count} profiles · {import.RuleCount} conditions");
        if (ImGui.SmallButton("Select all"))
        {
            import.Selected.UnionWith(import.Source.Profiles.Select(profile => profile.Id));
            _dirty = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Select none"))
        {
            import.Selected.Clear();
            _dirty = true;
        }

        var installed = ProfileTransfer.GetInstalledPack(editor.Config, import.Source);
        var localIds = editor.Config.Profiles.Select(profile => profile.Id).ToHashSet();
        var height = Math.Min(import.Source.Profiles.Count, 7) * (ImGui.GetFrameHeightWithSpacing() + 4f * ImGuiHelpers.GlobalScale);
        using var child = ImRaii.Child("Import selection", new Vector2(0, height));
        if (!child) return;
        using var table = ImRaii.Table("Import profiles", 3, ImGuiTableFlags.SizingStretchProp);
        if (!table) return;
        ImGui.TableSetupColumn("Include", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Change", ImGuiTableColumnFlags.WidthFixed, 60f * ImGuiHelpers.GlobalScale);
        foreach (var profile in import.Source.Profiles)
        {
            using var id = ImRaii.PushId(profile.Id.ToString());
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var selected = import.Selected.Contains(profile.Id);
            if (ImGui.Checkbox("##Include", ref selected))
            {
                if (selected) import.Selected.Add(profile.Id);
                else import.Selected.Remove(profile.Id);
                _dirty = true;
            }

            ImGui.TableNextColumn();
            using var disabled = ImRaii.Disabled(!selected);
            var name = import.Names[profile.Id];
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##Name", ref name, 81))
            {
                import.Names[profile.Id] = name;
                _dirty = true;
            }

            ImGui.TableNextColumn();
            var update = Mode == ImportMode.Update && installed is not null
                         && installed.ProfileIds.TryGetValue(profile.Id, out var localId) && localIds.Contains(localId);
            ImGui.TextDisabled(update ? "Update" : "Add");
        }
    }

    internal static void Warning(string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationCircle.ToIconString());
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }
}
