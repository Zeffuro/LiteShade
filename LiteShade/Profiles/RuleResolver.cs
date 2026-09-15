using System;
using System.Collections.Generic;

namespace LiteShade.Profiles;

public enum RuleCondition
{
    Time,
    Zone,
    Area,
    Weather,
    Duty,
    Combat,
    GPose,
    Cutscene,
    IdleCamera,
    Crafting,
    Gathering,
    Mounted,
    Performing,
}

public static class RuleResolver
{
    public static ProfileSelection Resolve(
        IReadOnlyList<ProfileRule> rules,
        IReadOnlySet<Guid> profileIds,
        Guid fallbackProfileId,
        ProfileContext context,
        bool automaticSelectionEnabled)
    {
        if (!automaticSelectionEnabled || !context.IsLoggedIn || context.IsTransitioning)
        {
            return new ProfileSelection(fallbackProfileId, null);
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule is null || !rule.Enabled || !rule.IsValid || !profileIds.Contains(rule.ProfileId))
            {
                continue;
            }

            if (GetMismatch(rule, context) is not null)
            {
                continue;
            }

            return new ProfileSelection(rule.ProfileId, rule.Id);
        }

        return new ProfileSelection(fallbackProfileId, null);
    }

    public static RuleCondition? GetMismatch(ProfileRule rule, ProfileContext context)
    {
        if (rule.StartTime is { } start && rule.EndTime is { } end && start != end)
        {
            if (context.DayTimeSeconds is not { } seconds)
            {
                return RuleCondition.Time;
            }

            var minute = (int)(seconds / 60);
            var inRange = start < end ? minute >= start && minute < end : minute >= start || minute < end;
            if (!inRange)
            {
                return RuleCondition.Time;
            }
        }

        if (rule.TerritoryId is { } territoryId && territoryId != context.TerritoryId)
        {
            return RuleCondition.Zone;
        }

        if (rule.AreaId is { } areaId && context.AreaId != areaId)
        {
            return RuleCondition.Area;
        }

        if (rule.WeatherId is { } weatherId && context.WeatherId != weatherId)
        {
            return RuleCondition.Weather;
        }

        if (rule.Activity == RuleActivity.Overworld && context.InDuty)
        {
            return RuleCondition.Duty;
        }

        if (rule.Activity == RuleActivity.Duty && !context.InDuty)
        {
            return RuleCondition.Duty;
        }

        if (rule.InCombat is { } combat && combat != context.InCombat) return RuleCondition.Combat;
        if (rule.InGPose is { } gpose && gpose != context.InGPose) return RuleCondition.GPose;
        if (rule.InCutscene is { } cutscene && cutscene != context.InCutscene) return RuleCondition.Cutscene;
        if (rule.InIdleCamera is { } idle && idle != context.InIdleCamera) return RuleCondition.IdleCamera;
        if (rule.IsCrafting is { } crafting && crafting != context.IsCrafting) return RuleCondition.Crafting;
        if (rule.IsGathering is { } gathering && gathering != context.IsGathering) return RuleCondition.Gathering;
        if (rule.IsMounted is { } mounted && mounted != context.IsMounted) return RuleCondition.Mounted;
        if (rule.IsPerforming is { } performing && performing != context.IsPerforming) return RuleCondition.Performing;
        return null;
    }
}
