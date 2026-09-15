using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Profiles;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private static readonly string[] DutyChoices = ["Any", "Outside duty", "In duty"];
    private static readonly string[] StateChoices = ["Any", "Yes", "No"];

    private string _conditionSearch = string.Empty;
    private bool _showArea;
    private Guid? _timeRuleId;
    private Guid? _invalidTimeRuleId;
    private string _startTimeText = string.Empty;
    private string _endTimeText = string.Empty;

    private void DrawLocationConditions(ProfileRule rule)
    {
        var context = _profiles.Context;
        var currentAvailable = context is { IsLoggedIn: true, IsTransitioning: false };
        var zone = rule.TerritoryId;
        if (LocationCombo("Zone", ref zone, _names.Territories, currentAvailable ? context.TerritoryId : null))
        {
            rule.TerritoryId = zone;
            rule.AreaId = null;
            _showArea = false;
            Save();
        }

        if (rule.TerritoryId.HasValue && DrawRemoveButton("Zone"))
        {
            rule.TerritoryId = null;
            rule.AreaId = null;
            _showArea = false;
            Save();
        }

        uint? weather = rule.WeatherId;
        if (LocationCombo("Weather", ref weather, _names.Weathers, currentAvailable ? context.WeatherId : null))
        {
            rule.WeatherId = weather.HasValue ? (byte)weather.Value : null;
            Save();
        }

        if (rule.WeatherId.HasValue && DrawRemoveButton("Weather"))
        {
            rule.WeatherId = null;
            Save();
        }

        if (rule.AreaId.HasValue || _showArea)
        {
            var area = rule.AreaId;
            if (LocationCombo("Area", ref area, _names.Areas, currentAvailable ? context.AreaId : null))
            {
                rule.AreaId = area;
                _showArea = area.HasValue;
                Save();
            }

            if ((rule.AreaId.HasValue || _showArea) && DrawRemoveButton("Area"))
            {
                rule.AreaId = null;
                _showArea = false;
                Save();
            }
        }
    }

    private bool LocationCombo(string label, ref uint? selected, IReadOnlyDictionary<uint, string> choices, uint? current)
    {
        using var id = ImRaii.PushId(label);
        using var combo = ImRaii.Combo(label, selected is { } value
            ? choices.GetValueOrDefault(value) ?? $"Unknown {label.ToLowerInvariant()} ({value})"
            : "Any");
        if (!combo)
        {
            return false;
        }

        if (ImGui.IsWindowAppearing())
        {
            _conditionSearch = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.InputTextWithHint("##Search", "Search", ref _conditionSearch, 80);
        if (ImGui.Selectable("Any", selected is null))
        {
            selected = null;
            return true;
        }

        if (current is > 0)
        {
            var currentName = choices.GetValueOrDefault(current.Value) ?? $"Unknown {label.ToLowerInvariant()} ({current.Value})";
            if (ImGui.Selectable($"Current: {currentName}"))
            {
                selected = current;
                return true;
            }
        }

        ImGui.Separator();
        uint.TryParse(_conditionSearch, out var searchId);
        foreach (var (key, name) in choices)
        {
            if (!name.Contains(_conditionSearch, StringComparison.OrdinalIgnoreCase) && key != searchId)
            {
                continue;
            }

            using var rowId = ImRaii.PushId((int)key);
            if (ImGui.Selectable(name, selected == key))
            {
                selected = key;
                return true;
            }
        }

        return false;
    }

    private void DrawExtraConditions(ProfileRule rule)
    {
        DrawDutyCondition(rule);
        DrawStateCondition("Combat", rule.InCombat, value => rule.InCombat = value);
        DrawStateCondition("GPose", rule.InGPose, value => rule.InGPose = value);
        DrawStateCondition("Cutscene", rule.InCutscene, value => rule.InCutscene = value);
        DrawStateCondition("Idle camera", rule.InIdleCamera, value => rule.InIdleCamera = value);
        DrawStateCondition("Crafting", rule.IsCrafting, value => rule.IsCrafting = value);
        DrawStateCondition("Gathering", rule.IsGathering, value => rule.IsGathering = value);
        DrawStateCondition("Mounted", rule.IsMounted, value => rule.IsMounted = value);
        DrawStateCondition("Performing", rule.IsPerforming, value => rule.IsPerforming = value);
        DrawTimeCondition(rule);

        if (ImGui.Button("Add condition"))
        {
            ImGui.OpenPopup("AddCondition");
        }

        using var popup = ImRaii.Popup("AddCondition");
        if (!popup)
        {
            return;
        }

        if (!rule.AreaId.HasValue && !_showArea && ImGui.Selectable("Area"))
        {
            _showArea = true;
        }

        if (rule.Activity == RuleActivity.Any && ImGui.Selectable("Duty"))
        {
            rule.Activity = RuleActivity.Duty;
            Save();
        }

        AddState("Combat", rule.InCombat, () => rule.InCombat = true);
        AddState("GPose", rule.InGPose, () => rule.InGPose = true);
        AddState("Cutscene", rule.InCutscene, () => rule.InCutscene = true);
        AddState("Idle camera", rule.InIdleCamera, () => rule.InIdleCamera = true);
        AddState("Crafting", rule.IsCrafting, () => rule.IsCrafting = true);
        AddState("Gathering", rule.IsGathering, () => rule.IsGathering = true);
        AddState("Mounted", rule.IsMounted, () => rule.IsMounted = true);
        AddState("Performing", rule.IsPerforming, () => rule.IsPerforming = true);

        if (!rule.StartTime.HasValue && !rule.EndTime.HasValue && ImGui.Selectable("Time (ET)"))
        {
            rule.StartTime = 6 * 60;
            rule.EndTime = 18 * 60;
            _timeRuleId = null;
            Save();
        }
    }

    private void DrawDutyCondition(ProfileRule rule)
    {
        if (rule.Activity == RuleActivity.Any)
        {
            return;
        }

        var activity = (int)rule.Activity;
        if (ImGui.Combo("Duty", ref activity, DutyChoices, DutyChoices.Length))
        {
            rule.Activity = (RuleActivity)activity;
            Save();
        }

        if (rule.Activity != RuleActivity.Any && DrawRemoveButton("Duty"))
        {
            rule.Activity = RuleActivity.Any;
            Save();
        }
    }

    private void DrawStateCondition(string label, bool? value, Action<bool?> set)
    {
        if (!value.HasValue)
        {
            return;
        }

        var selected = value.Value ? 1 : 2;
        if (ImGui.Combo(label, ref selected, StateChoices, StateChoices.Length))
        {
            set(selected == 0 ? null : selected == 1);
            Save();
        }

        if (selected != 0 && DrawRemoveButton(label))
        {
            set(null);
            Save();
        }
    }

    private void DrawTimeCondition(ProfileRule rule)
    {
        if (!rule.StartTime.HasValue && !rule.EndTime.HasValue)
        {
            return;
        }

        if (_timeRuleId != rule.Id)
        {
            _timeRuleId = rule.Id;
            _startTimeText = FormatEtTime(rule.StartTime ?? 6 * 60);
            _endTimeText = FormatEtTime(rule.EndTime ?? 18 * 60);
        }

        ImGui.TextUnformatted("Time (ET)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Eorzea time. Ranges can cross midnight. Matching times cover the whole day.");
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(64 * ImGuiHelpers.GlobalScale);
        var changed = ImGui.InputText("##Start", ref _startTimeText, 6);
        ImGui.SameLine();
        ImGui.TextUnformatted("to");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(64 * ImGuiHelpers.GlobalScale);
        changed |= ImGui.InputText("##End", ref _endTimeText, 6);
        if (changed && TryParseEtTime(_startTimeText, out var start) && TryParseEtTime(_endTimeText, out var end))
        {
            rule.StartTime = start;
            rule.EndTime = end;
            _invalidTimeRuleId = null;
            Save();
        }
        else if (changed)
        {
            _invalidTimeRuleId = rule.Id;
        }

        if (_invalidTimeRuleId == rule.Id)
        {
            ImGui.TextDisabled("Invalid time. Use HH:mm");
        }

        if (DrawRemoveButton("Time (ET)"))
        {
            rule.StartTime = null;
            rule.EndTime = null;
            _timeRuleId = null;
            _invalidTimeRuleId = null;
            Save();
        }
    }

    private static bool TryParseEtTime(string text, out int minutes)
    {
        minutes = 0;
        if (text.Length != 5 || text[2] != ':' || text[0] is < '0' or > '9' || text[1] is < '0' or > '9'
            || text[3] is < '0' or > '9' || text[4] is < '0' or > '9')
        {
            return false;
        }

        var hour = (text[0] - '0') * 10 + (text[1] - '0');
        var minute = (text[3] - '0') * 10 + (text[4] - '0');
        if (hour > 23 || minute > 59)
        {
            return false;
        }

        minutes = hour * 60 + minute;
        return true;
    }

    private static string FormatEtTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private bool DrawRemoveButton(string label)
    {
        ImGui.SameLine();
        var remove = ImGui.SmallButton($"Remove##{label}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Remove {label.ToLowerInvariant()} condition");
        }

        return remove;
    }

    private void AddState(string label, bool? value, Action add)
    {
        if (value.HasValue || !ImGui.Selectable(label))
        {
            return;
        }

        add();
        Save();
    }
}
