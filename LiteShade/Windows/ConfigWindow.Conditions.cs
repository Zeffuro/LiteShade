using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Profiles;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private bool _scrollToRule;

    private void DrawConditionEditor()
    {
        var automatic = _config.AutomaticProfiles;
        if (ImGui.Checkbox("Switch profiles automatically", ref automatic))
        {
            _config.AutomaticProfiles = automatic;
            Save();
        }

        ImGui.TextDisabled("The first match from the top is used. Otherwise, your default profile is used.");
        if (ImGui.Button("New rule"))
        {
            AddRule(false);
        }

        ImGui.SameLine();
        var context = _profiles.Context;
        using (ImRaii.Disabled(!context.IsLoggedIn || context.IsTransitioning || context.TerritoryId == 0 || !context.WeatherId.HasValue))
        {
            if (ImGui.Button("Use current zone and weather"))
            {
                AddRule(true);
            }
        }

        if (_config.Rules.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Add a rule, choose a profile, then choose when it should be used.");
            return;
        }

        if (!_config.Rules.Any(rule => rule.Id == _editingRuleId))
        {
            _editingRuleId = _config.Rules[0].Id;
            _showArea = false;
        }

        DrawRuleList();
        var editing = _config.Rules.First(rule => rule.Id == _editingRuleId);
        var index = _config.Rules.IndexOf(editing);
        using (ImRaii.Disabled(index == 0))
        {
            if (ImGui.Button("Move up"))
            {
                MoveRule(index, index - 1);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(index == _config.Rules.Count - 1))
        {
            if (ImGui.Button("Move down"))
            {
                MoveRule(index, index + 1);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Duplicate rule"))
        {
            var copy = editing.Copy();
            copy.Id = Guid.NewGuid();
            copy.Name += " copy";
            copy.Enabled = false;
            _config.Rules.Insert(_config.Rules.IndexOf(editing) + 1, copy);
            _editingRuleId = copy.Id;
            _showArea = false;
            _scrollToRule = true;
            Save();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete rule"))
        {
            _config.Rules.Remove(editing);
            _editingRuleId = _config.Rules.ElementAtOrDefault(Math.Min(index, _config.Rules.Count - 1))?.Id;
            _showArea = false;
            _scrollToRule = true;
            Save();
            return;
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"Position {_config.Rules.IndexOf(editing) + 1} of {_config.Rules.Count}");
        ImGui.Separator();
        DrawRule(editing);
    }

    private void DrawRuleList()
    {
        using var table = ImRaii.Table("Rules", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 7));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 40 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Profile", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Matches (lower priority)").X);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var context = _profiles.Context;
        for (var i = 0; i < _config.Rules.Count; i++)
        {
            var rule = _config.Rules[i];
            using var id = ImRaii.PushId(rule.Id.ToString());
            ImGui.TableNextRow();
            if (rule.Id == _editingRuleId)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted((i + 1).ToString());
            ImGui.TableNextColumn();
            using var selectedColour = ImRaii.PushColor(ImGuiCol.Header, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]);
            if (ImGui.Selectable(rule.Name + "###Rule", rule.Id == _editingRuleId, ImGuiSelectableFlags.SpanAllColumns))
            {
                _editingRuleId = rule.Id;
                _showArea = false;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(RuleSummary(rule));
            }

            if (_scrollToRule && rule.Id == _editingRuleId)
            {
                ImGui.SetScrollHereY();
                _scrollToRule = false;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(_config.Profiles.FirstOrDefault(profile => profile.Id == rule.ProfileId)?.Name ?? "Missing profile");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(RuleListStatus(rule, context));
        }
    }

    private string RuleListStatus(ProfileRule rule, ProfileContext context)
    {
        if (!rule.Enabled || !rule.IsValid || _config.Profiles.All(profile => profile.Id != rule.ProfileId))
        {
            return "Disabled";
        }

        if (!context.IsLoggedIn || context.IsTransitioning || RuleResolver.GetMismatch(rule, context) is not null)
        {
            return "No match";
        }

        if (!_config.Enabled || !_config.AutomaticProfiles || _profiles.OverrideProfileId.HasValue)
        {
            return "Matches";
        }

        return _profiles.Selection.RuleId == rule.Id ? "Active" : "Matches (lower priority)";
    }

    private void AddRule(bool useCurrent)
    {
        var context = _profiles.Context;
        if (useCurrent && (!context.IsLoggedIn || context.IsTransitioning || context.TerritoryId == 0 || !context.WeatherId.HasValue))
        {
            return;
        }

        var rule = new ProfileRule
        {
            Name = useCurrent ? _names.Territory(context.TerritoryId) : $"Rule {_config.Rules.Count + 1}",
            ProfileId = _editingProfileId,
            TerritoryId = useCurrent ? context.TerritoryId : null,
            WeatherId = useCurrent ? context.WeatherId : null,
            Enabled = false,
        };
        if (_config.Profiles.All(profile => profile.Id != rule.ProfileId))
        {
            rule.ProfileId = _config.DefaultProfileId;
        }

        _config.Rules.Add(rule);
        _editingRuleId = rule.Id;
        _showArea = false;
        _scrollToRule = true;
        Save();
    }

    private void MoveRule(int from, int to)
    {
        (_config.Rules[from], _config.Rules[to]) = (_config.Rules[to], _config.Rules[from]);
        _scrollToRule = true;
        Save();
    }

    private void DrawRule(ProfileRule rule)
    {
        using var id = ImRaii.PushId(rule.Id.ToString());
        var enabled = rule.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            rule.Enabled = enabled;
            Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var name = rule.Name;
        if (ImGui.InputText("##Name", ref name, 81))
        {
            rule.Name = name;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
        }

        var profile = rule.ProfileId;
        if (ProfileCombo("Use profile", ref profile))
        {
            rule.ProfileId = profile;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("When all of these match:");
        DrawLocationConditions(rule);
        DrawExtraConditions(rule);
        ImGui.Spacing();
        ImGui.TextDisabled(RuleStatus(rule));
    }

    private string RuleSummary(ProfileRule rule)
    {
        var parts = new List<string>();
        if (rule.TerritoryId is { } zone) parts.Add(_names.Territory(zone));
        if (rule.AreaId is { } area) parts.Add(_names.Area(area));
        if (rule.WeatherId is { } weather) parts.Add(_names.Weather(weather));
        if (rule.StartTime is { } start && rule.EndTime is { } end)
        {
            parts.Add(start == end ? "All day (ET)" : $"{FormatEtTime(start)}–{FormatEtTime(end)} ET");
        }

        if (rule.Activity != RuleActivity.Any) parts.Add(rule.Activity == RuleActivity.Duty ? "In duty" : "Outside duty");
        Add("Combat", rule.InCombat);
        Add("GPose", rule.InGPose);
        Add("Cutscene", rule.InCutscene);
        Add("Idle camera", rule.InIdleCamera);
        Add("Crafting", rule.IsCrafting);
        Add("Gathering", rule.IsGathering);
        Add("Mounted", rule.IsMounted);
        Add("Performing", rule.IsPerforming);
        return parts.Count == 0 ? "Always" : string.Join(" · ", parts);

        void Add(string label, bool? value)
        {
            if (value is { } state) parts.Add($"{label}: {(state ? "yes" : "no")}");
        }
    }

    private string RuleStatus(ProfileRule rule)
    {
        if (!rule.Enabled) return "Rule disabled";
        if (!rule.IsValid) return "Incomplete condition";
        if (_config.Profiles.All(profile => profile.Id != rule.ProfileId)) return "Profile is missing";
        var context = _profiles.Context;
        if (!context.IsLoggedIn || context.IsTransitioning) return "Waiting for the scene";

        var mismatch = RuleResolver.GetMismatch(rule, context);
        if (mismatch is { } condition)
        {
            var reason = condition switch
            {
                RuleCondition.Time => context.DayTimeSeconds is { } seconds ? $"time is {FormatEtTime((int)(seconds / 60))} ET" : "time is unavailable",
                RuleCondition.Zone => $"zone is {_names.Territory(context.TerritoryId)}",
                RuleCondition.Area => context.AreaId is { } area ? $"area is {_names.Area(area)}" : "area is unavailable",
                RuleCondition.Weather => context.WeatherId is { } weather ? $"weather is {_names.Weather(weather)}" : "weather is unavailable",
                RuleCondition.Duty => context.InDuty ? "in duty" : "outside duty",
                RuleCondition.Combat => context.InCombat ? "in combat" : "outside combat",
                RuleCondition.GPose => context.InGPose ? "in GPose" : "outside GPose",
                RuleCondition.Cutscene => context.InCutscene ? "in a cutscene" : "outside cutscenes",
                RuleCondition.IdleCamera => context.InIdleCamera ? "idle camera active" : "idle camera inactive",
                RuleCondition.Crafting => context.IsCrafting ? "crafting" : "not crafting",
                RuleCondition.Gathering => context.IsGathering ? "gathering" : "not gathering",
                RuleCondition.Mounted => context.IsMounted ? "mounted" : "not mounted",
                RuleCondition.Performing => context.IsPerforming ? "performing" : "not performing",
                _ => "condition differs",
            };
            return $"Not matching: {reason}";
        }

        if (!_config.Enabled) return "Matches. LiteShade is disabled";
        if (_profiles.OverrideProfileId.HasValue) return "Matches. A profile override is active";
        if (!_config.AutomaticProfiles) return "Matches. Automatic selection is off";
        var selection = _profiles.Selection;
        if (selection.RuleId == rule.Id) return "In use";
        var earlier = _config.Rules.FirstOrDefault(item => item.Id == selection.RuleId);
        return earlier is null ? "Matches" : $"Matches. {earlier.Name} takes priority";
    }
}
