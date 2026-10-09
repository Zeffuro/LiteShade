using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Profiles;

namespace LiteShade.Windows.Components;

internal sealed class ConditionList(SystemConfiguration config, ContextNames names, Action save)
{
    private readonly HashSet<Guid> _selected = [];
    private string _search = string.Empty;
    private Guid? _profile;
    private string? _folder;
    private uint? _zone;

    public void Reset()
    {
        _selected.Clear();
        _search = string.Empty;
        _profile = null;
        _folder = null;
        _zone = null;
    }

    public void Draw(ref Guid? editing, ref bool scroll, Action selected, Func<ProfileRule, string> summary,
        Func<ProfileRule, bool, string> status, string? activeRule)
    {
        var profiles = config.Profiles.ToDictionary(profile => profile.Id);
        DrawFilters(profiles);
        var visible = config.Rules.Select((rule, index) => (rule, index))
            .Where(row => Matches(row.rule, profiles.GetValueOrDefault(row.rule.ProfileId))).ToArray();
        _selected.IntersectWith(visible.Select(row => row.rule.Id));
        var editingId = editing;
        if (editingId.HasValue && visible.All(row => row.rule.Id != editingId))
        {
            ImGui.TextDisabled("The editing rule is hidden by your filters.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Show selected")) Reset();
        }

        using (var table = ImRaii.Table("Condition rules", 5,
                   ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                   new Vector2(0, ImGui.GetFrameHeightWithSpacing() * 7)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##Select", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 40 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Profile", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Lower priority").X + ImGui.GetFrameHeight());
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                var clipper = ImGui.ImGuiListClipper();
                try
                {
                    clipper.Begin(visible.Length);
                    if (scroll)
                    {
                        var position = Array.FindIndex(visible, row => row.rule.Id == editingId);
                        if (position >= 0) clipper.ForceDisplayRangeByIndices(position, position + 1);
                    }

                    while (clipper.Step())
                    {
                        for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                        {
                            var (rule, index) = visible[row];
                            using var id = ImRaii.PushId(rule.Id.ToString());
                            ImGui.TableNextRow();
                            if (rule.Id == editing)
                                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
                            ImGui.TableNextColumn();
                            var included = _selected.Contains(rule.Id);
                            if (ImGui.Checkbox("##Select", ref included))
                            {
                                if (included) _selected.Add(rule.Id);
                                else _selected.Remove(rule.Id);
                            }

                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted((index + 1).ToString());
                            ImGui.TableNextColumn();
                            if (ImGui.Selectable(rule.Name + "###Rule", rule.Id == editing))
                            {
                                editing = rule.Id;
                                selected();
                            }

                            if (ImGui.IsItemHovered()) ImGui.SetTooltip(summary(rule));
                            if (scroll && rule.Id == editing)
                            {
                                ImGui.SetScrollHereY();
                                scroll = false;
                            }

                            ImGui.TableNextColumn();
                            var profile = profiles.GetValueOrDefault(rule.ProfileId);
                            ImGui.TextUnformatted(profile?.Name ?? "Missing profile");
                            ImGui.TableNextColumn();
                            var state = status(rule, profile is not null);
                            ImGui.TextUnformatted(state);
                            if (state == "Lower priority")
                            {
                                ImGui.SameLine();
                                using (ImRaii.PushFont(UiBuilder.IconFont))
                                    ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{activeRule ?? "An earlier rule"} takes priority.");
                            }
                        }
                    }
                }
                finally
                {
                    clipper.Destroy();
                }
            }
        }

        ImGui.TextDisabled($"{visible.Length} / {config.Rules.Count} rules");
        ImGui.SameLine();
        if (ImGui.SmallButton("Select shown")) _selected.UnionWith(visible.Select(row => row.rule.Id));
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear selection")) _selected.Clear();
        if (_selected.Count == 0) return;
        ImGui.TextUnformatted($"{_selected.Count} selected");
        ImGui.SameLine();
        if (ImGui.SmallButton("Enable selected")) SetEnabled(true);
        ImGui.SameLine();
        if (ImGui.SmallButton("Disable selected")) SetEnabled(false);
    }

    private void DrawFilters(Dictionary<Guid, ColorProfile> profiles)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##Rule search", "Search rules, profiles, zones or weather", ref _search, 128);
        using var table = ImRaii.Table("Rule filters", 3, ImGuiTableFlags.SizingStretchSame);
        if (!table) return;
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##Profile filter", _profile.HasValue ? profiles.GetValueOrDefault(_profile.Value)?.Name ?? "Missing profile" : "Any profile"))
        {
            if (combo)
            {
                if (ImGui.Selectable("Any profile", !_profile.HasValue)) _profile = null;
                foreach (var profile in profiles.Values.OrderBy(profile => profile.Name))
                {
                    using var id = ImRaii.PushId(profile.Id.ToString());
                    if (ImGui.Selectable(profile.Name, _profile == profile.Id)) _profile = profile.Id;
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##Folder filter", _folder is null ? "Any folder" : _folder.Length == 0 ? "No folder" : _folder))
        {
            if (combo)
            {
                if (ImGui.Selectable("Any folder", _folder is null)) _folder = null;
                if (ImGui.Selectable("No folder", _folder == string.Empty)) _folder = string.Empty;
                foreach (var folder in profiles.Values.Select(profile => profile.Folder).Where(folder => folder.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order())
                {
                    using var id = ImRaii.PushId(folder);
                    if (ImGui.Selectable(folder, _folder == folder)) _folder = folder;
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##Zone filter", _zone.HasValue ? names.Territory(_zone.Value) : "Any zone"))
        {
            if (combo)
            {
                if (ImGui.Selectable("Any zone", !_zone.HasValue)) _zone = null;
                foreach (var zone in config.Rules.SelectMany(rule => rule.TerritoryIds).Distinct().OrderBy(names.Territory))
                {
                    using var id = ImRaii.PushId(zone.ToString());
                    if (ImGui.Selectable(names.Territory(zone), _zone == zone)) _zone = zone;
                }
            }
        }
    }

    private bool Matches(ProfileRule rule, ColorProfile? profile)
    {
        if (_profile.HasValue && rule.ProfileId != _profile) return false;
        if (_folder is not null && !string.Equals(profile?.Folder, _folder, StringComparison.OrdinalIgnoreCase)) return false;
        if (_zone.HasValue && !rule.TerritoryIds.Contains(_zone.Value)) return false;
        if (_search.Length == 0) return true;
        return Contains(rule.Name) || Contains(profile?.Name) || Contains(profile?.Folder)
               || rule.TerritoryIds.Any(zone => Contains(names.Territory(zone)))
               || rule.AreaId is { } area && Contains(names.Area(area))
               || rule.WeatherIds.Any(weather => Contains(names.Weather(weather)));
    }

    private bool Contains(string? text) => text?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true;

    private void SetEnabled(bool enabled)
    {
        foreach (var rule in config.Rules.Where(rule => _selected.Contains(rule.Id))) rule.Enabled = enabled;
        save();
    }
}
