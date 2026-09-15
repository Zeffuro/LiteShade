using System;
using System.Collections.Generic;

namespace LiteShade.Profiles;

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

            if (!Matches(rule, context))
            {
                continue;
            }

            return new ProfileSelection(rule.ProfileId, rule.Id);
        }

        return new ProfileSelection(fallbackProfileId, null);
    }

    private static bool Matches(ProfileRule rule, ProfileContext context)
    {
        if (rule.StartTime is { } start && rule.EndTime is { } end && start != end)
        {
            if (context.DayTimeSeconds is not { } seconds)
            {
                return false;
            }

            var minute = (int)(seconds / 60);
            var inRange = start < end ? minute >= start && minute < end : minute >= start || minute < end;
            if (!inRange)
            {
                return false;
            }
        }

        if (rule.TerritoryId is { } territoryId && territoryId != context.TerritoryId)
        {
            return false;
        }

        if (rule.AreaId is { } areaId && context.AreaId != areaId)
        {
            return false;
        }

        if (rule.WeatherId is { } weatherId && context.WeatherId != weatherId)
        {
            return false;
        }

        if (rule.Activity == RuleActivity.Overworld && context.InDuty)
        {
            return false;
        }

        if (rule.Activity == RuleActivity.Duty && !context.InDuty)
        {
            return false;
        }

        return (rule.InCombat is null || rule.InCombat == context.InCombat)
               && (rule.InGPose is null || rule.InGPose == context.InGPose)
               && (rule.InCutscene is null || rule.InCutscene == context.InCutscene)
               && (rule.InIdleCamera is null || rule.InIdleCamera == context.InIdleCamera)
               && (rule.IsCrafting is null || rule.IsCrafting == context.IsCrafting)
               && (rule.IsGathering is null || rule.IsGathering == context.IsGathering)
               && (rule.IsMounted is null || rule.IsMounted == context.IsMounted)
               && (rule.IsPerforming is null || rule.IsPerforming == context.IsPerforming);
    }
}
