using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using LiteShade.Configuration;

namespace LiteShade.Graphics;

internal static unsafe class EffectPauseState
{
    public static PauseOptions GetActive(GraphicsConfig* graphics, ICondition conditions)
    {
        var active = PauseOptions.None;
        var inGPose = GameMain.IsInGPose();
        if (inGPose) active |= PauseOptions.GPose;
        if (!inGPose && (conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78]
            || conditions[ConditionFlag.OccupiedInCutSceneEvent])) active |= PauseOptions.Cutscenes;
        if (graphics->PortraitMode || graphics->PortraitPreview) active |= PauseOptions.Portraits;
        if (GameMain.IsInIdleCam()) active |= PauseOptions.IdleCamera;
        if (conditions[ConditionFlag.InCombat]) active |= PauseOptions.Combat;
        return active;
    }
}
