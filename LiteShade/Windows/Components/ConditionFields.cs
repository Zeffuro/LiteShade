using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Profiles;

namespace LiteShade.Windows.Components;

internal sealed class ConditionFields
{
    private static readonly string[] DutyChoices = ["Any", "Outside duty", "In duty"];
    private static readonly string[] StateChoices = ["Any", "Yes", "No"];

    private readonly ProfileService _profiles;
    private readonly ContextNames _names;
    private readonly Action _save;
    private string _conditionSearch = string.Empty;
    private Guid? _timeRuleId;
    private Guid? _invalidTimeRuleId;
    private string _startTimeText = string.Empty;
    private string _endTimeText = string.Empty;

    public bool ShowArea { get; set; }

    public ConditionFields(ProfileService profiles, ContextNames names, Action save)
    {
        _profiles = profiles;
        _names = names;
        _save = save;
    }

    public void Reset()
    {
        ShowArea = false;
        _conditionSearch = string.Empty;
        _timeRuleId = _invalidTimeRuleId = null;
    }

    public void DrawLocation(ProfileRule rule)
    {
        var context = _profiles.Context;
        var currentAvailable = context is { IsLoggedIn: true, IsTransitioning: false };
        if (DrawChoices("Zone", rule.TerritoryIds, _names.Territories,
                currentAvailable && context.TerritoryId != 0 ? context.TerritoryId : null))
        {
            rule.AreaId = null;
            ShowArea = false;
            _save();
        }

        if (rule.TerritoryIds.Count > 0 && DrawRemoveButton("Zone"))
        {
            rule.TerritoryIds = [];
            rule.AreaId = null;
            ShowArea = false;
            _save();
        }

        if (DrawChoices("Weather", rule.WeatherIds, _names.Weathers,
                currentAvailable && context.WeatherId is > 0 ? context.WeatherId : null))
        {
            _save();
        }

        if (rule.WeatherIds.Count > 0 && DrawRemoveButton("Weather"))
        {
            rule.WeatherIds = [];
            _save();
        }

        if (rule.AreaId.HasValue || ShowArea)
        {
            var area = rule.AreaId;
            if (LocationCombo("Area", ref area, _names.Areas, currentAvailable ? context.AreaId : null))
            {
                rule.AreaId = area;
                ShowArea = area.HasValue;
                _save();
            }

            if ((rule.AreaId.HasValue || ShowArea) && DrawRemoveButton("Area"))
            {
                rule.AreaId = null;
                ShowArea = false;
                _save();
            }
        }
    }

    private bool DrawChoices<T>(string label, List<T> selected, IReadOnlyDictionary<T, string> choices,
        T? current) where T : struct
    {
        using var id = ImRaii.PushId(label);
        var preview = FormatSelection(selected, Name);
        using var combo = ImRaii.Combo(label, preview);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(selected.Count > 0
                ? preview
                : $"Matches any {label.ToLowerInvariant()}.");
        }
        if (!combo) return false;

        if (ImGui.IsWindowAppearing())
        {
            _conditionSearch = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.InputTextWithHint("##Search", "Search", ref _conditionSearch, 80);
        ImGui.TextDisabled($"Matches any selected {label.ToLowerInvariant()}.");
        var changed = false;
        if (ImGui.Selectable("Any", selected.Count == 0, ImGuiSelectableFlags.DontClosePopups))
        {
            selected.Clear();
            changed = true;
        }

        if (current is { } currentId && ImGui.Selectable($"Current: {Name(currentId)}",
                selected.Contains(currentId), ImGuiSelectableFlags.DontClosePopups))
        {
            Toggle(currentId);
        }

        ImGui.Separator();
        var searchId = uint.TryParse(_conditionSearch, out var number) ? number.ToString() : null;
        var unknown = selected.Where(value => !choices.ContainsKey(value))
            .Select(value => KeyValuePair.Create(value, Name(value))).ToArray();
        var rows = choices.Concat(unknown);
        foreach (var (key, name) in rows)
        {
            var keyText = key.ToString()!;
            if (!name.Contains(_conditionSearch, StringComparison.OrdinalIgnoreCase) && keyText != searchId) continue;
            using var rowId = ImRaii.PushId(keyText);
            if (ImGui.Selectable(name, selected.Contains(key), ImGuiSelectableFlags.DontClosePopups))
            {
                Toggle(key);
            }
        }

        return changed;

        string Name(T value) => choices.GetValueOrDefault(value)
            ?? $"Unknown {label.ToLowerInvariant()} ({value})";

        void Toggle(T value)
        {
            if (!selected.Remove(value)) selected.Add(value);
            changed = true;
        }
    }

    public static string FormatSelection<T>(IReadOnlyList<T> selected, Func<T, string> name) => selected.Count switch
    {
        0 => "Any",
        1 => name(selected[0]),
        _ => $"{selected.Count}: {string.Join(", ", selected.Select(name))}",
    };

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

    public void DrawExtra(ProfileRule rule)
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

        if (!rule.AreaId.HasValue && !ShowArea && ImGui.Selectable("Area"))
        {
            ShowArea = true;
        }

        if (rule.Activity == RuleActivity.Any && ImGui.Selectable("Duty"))
        {
            rule.Activity = RuleActivity.Duty;
            _save();
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
            _save();
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
            _save();
        }

        if (rule.Activity != RuleActivity.Any && DrawRemoveButton("Duty"))
        {
            rule.Activity = RuleActivity.Any;
            _save();
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
            _save();
        }

        if (selected != 0 && DrawRemoveButton(label))
        {
            set(null);
            _save();
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
            _save();
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
            _save();
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

    public static string FormatEtTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

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
        _save();
    }
}
