using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using LiteShade.Helpers;

namespace LiteShade.Profiles;

public readonly record struct ProfileContext
{
    private static readonly ConditionFlag[] LoadingFlags = [ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51];
    private static readonly ConditionFlag[] DutyFlags = [ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95];
    private static readonly ConditionFlag[] CutsceneFlags = [ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent];
    private static readonly ConditionFlag[] CraftingFlags = [ConditionFlag.Crafting, ConditionFlag.ExecutingCraftingAction, ConditionFlag.PreparingToCraft];
    private static readonly ConditionFlag[] GatheringFlags = [ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction];
    private static readonly ConditionFlag[] MountFlags = [ConditionFlag.Mounted, ConditionFlag.RidingPillion, ConditionFlag.Mounting, ConditionFlag.Mounting71];

    public bool IsLoggedIn { get; init; }
    public bool IsTransitioning { get; init; }
    public uint TerritoryId { get; init; }
    public uint? AreaId { get; init; }
    public byte? WeatherId { get; init; }
    public float? DayTimeSeconds { get; init; }
    public bool InDuty { get; init; }
    public bool InCombat { get; init; }
    public bool InGPose { get; init; }
    public bool InCutscene { get; init; }
    public bool InIdleCamera { get; init; }
    public bool IsCrafting { get; init; }
    public bool IsGathering { get; init; }
    public bool IsMounted { get; init; }
    public bool IsPerforming { get; init; }

    internal static unsafe ProfileContext Read()
    {
        var client = IClientState.Get();
        var conditions = ICondition.Get();
        var context = new ProfileContext
        {
            IsLoggedIn = client.IsLoggedIn,
            IsTransitioning = conditions.Any(LoadingFlags),
            TerritoryId = client.TerritoryType,
        };

        if (!context.IsLoggedIn || context.IsTransitioning)
        {
            return context;
        }

        var territory = TerritoryInfo.Instance();
        var environment = EnvManager.Instance();
        var hasScene = environment != null && environment->EnvScene != null;
        var seconds = hasScene ? environment->DayTimeSeconds : -1;
        var inGPose = GameMain.IsInGPose();

        return context with
        {
            AreaId = territory != null && territory->AreaPlaceNameId != 0 ? territory->AreaPlaceNameId : null,
            WeatherId = hasScene && environment->ActiveWeather != 0 ? environment->ActiveWeather : null,
            DayTimeSeconds = float.IsFinite(seconds) && seconds is >= 0 and < 86400 ? seconds : null,
            InDuty = conditions.Any(DutyFlags),
            InCombat = conditions[ConditionFlag.InCombat],
            InGPose = inGPose,
            InCutscene = !inGPose && conditions.Any(CutsceneFlags),
            InIdleCamera = GameMain.IsInIdleCam(),
            IsCrafting = conditions.Any(CraftingFlags),
            IsGathering = conditions.Any(GatheringFlags),
            IsMounted = conditions.Any(MountFlags),
            IsPerforming = conditions[ConditionFlag.Performing],
        };
    }
}
