using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Common.Math;
using LiteShade.Configuration;
using LiteShade.Helpers;
using LiteShade.Profiles;

namespace LiteShade.Graphics;

internal sealed unsafe class DepthOfField : IDisposable
{
    public readonly record struct Settings(bool AutoFocus, float FocusDistance, float FNumber);

    private readonly ProfileService _profiles;

    private readonly IFramework _framework = IFramework.Get();
    private readonly IClientState _client = IClientState.Get();
    private readonly ICondition _conditions = ICondition.Get();

    // TODO: Swap to FFXIVClientStructs when in main Dalamud.
    private readonly Hook<Experimental.RenderDelegate>? _hook;
    private readonly Experimental.PostEffectManager** _manager;
    private readonly Experimental.PostEffectResources** _resources;

    private readonly nint* _cocVTable;
    private volatile string _status = "Disabled";

    public string Status => _status;

    public DepthOfField(ProfileService profiles)
    {
        _profiles = profiles;
        try
        {
            var scanner = ISigScanner.Get();

            _manager = (Experimental.PostEffectManager**)scanner.GetStaticAddressFromSig(Experimental.ManagerSignature);
            _resources = (Experimental.PostEffectResources**)scanner.GetStaticAddressFromSig(Experimental.ResourcesSignature);
            _cocVTable = (nint*)scanner.GetStaticAddressFromSig(Experimental.CocVTableSignature, Experimental.CocVTableOffset);
            _hook = IGameInteropProvider.Get().HookFromAddress<Experimental.RenderDelegate>(
                scanner.ScanText(Experimental.RenderSignature), Render);
            _hook.Enable();
        }
        catch (Exception exception)
        {
            Dispose();
            _status = "Native depth of field unavailable";
            IPluginLog.Get().Error(exception, "Could not initialize native depth of field.");
        }
    }

    public void Dispose() => _hook?.Dispose();

    private void Render(Manager* renderManager)
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            _hook!.Original(renderManager);
            return;
        }

        var (_, settings, pauses) = _profiles.RenderSettings;
        if (settings is not { } dof)
        {
            _status = "Disabled";
            _hook!.Original(renderManager);
            return;
        }

        if (!CanApply(renderManager, pauses))
        {
            _hook!.Original(renderManager);
            return;
        }

        var manager = *_manager;
        var coc = Experimental.GetReadyDepthOfField(manager, *_resources, _cocVTable);
        if (coc == null || !TryGetFocus(dof, out var distance)
            || !float.IsFinite(manager->DepthOfField.CocNormalizationDivisor) || manager->DepthOfField.CocNormalizationDivisor <= 0
            || !float.IsFinite(1f / manager->DepthOfField.CocNormalizationDivisor))
        {
            _status = "Waiting for depth of field resources";
            _hook!.Original(renderManager);
            return;
        }

        var original = manager->DepthOfField;
        var nativeEnabled = (manager->Flags & Experimental.PostEffectFlags.DepthOfField) != 0;

        manager->DepthOfField.UseUpdatedDepthOfField = true;
        manager->DepthOfField.UseManualDepthOfField = false;
        manager->DepthOfField.UseFocusDistanceOverride = true;
        manager->DepthOfField.FocusDistance = distance;
        manager->DepthOfField.FNumber = dof.FNumber;
        manager->DepthOfField.NearBlurRate = 1;
        manager->DepthOfField.FarBlurRate = 1;
        manager->DepthOfField.EffectMaskRate = 0.5f;
        manager->DepthOfField.PreviousFrameWeight = 0;
        manager->DepthOfField.PreviousRangeScale = 0.1f;

        manager->Flags |= Experimental.PostEffectFlags.DepthOfField;

        try
        {
            _hook!.Original(renderManager);
            _status = "Active";
        }
        finally
        {
            if (*_manager == manager)
            {
                manager->DepthOfField = original;
                if (!nativeEnabled)
                {
                    manager->Flags &= ~Experimental.PostEffectFlags.DepthOfField;
                }

                coc->ResetHistory = true;
            }
        }
    }

    private bool CanApply(Manager* manager, PauseOptions pauses)
    {
        if (!_client.IsLoggedIn || _conditions[ConditionFlag.BetweenAreas] || _conditions[ConditionFlag.BetweenAreas51]
            || manager->Is3DRenderingDisabled || ((Experimental.RenderManagerState*)manager)->InitializationFlags != uint.MaxValue
            || (manager->Views[(int)Manager.RenderViews.Main].Flags & 3) != 3
            || Experimental.GetMainCamera(manager) == null)
        {
            _status = "Waiting for the main scene";
            return false;
        }

        var graphics = GraphicsConfig.Instance();
        if (graphics == null)
        {
            _status = "Waiting for graphics settings";
            return false;
        }

        var inGPose = GameMain.IsInGPose();
        if (((pauses & PauseOptions.GPose) != 0 && inGPose)
            || ((pauses & PauseOptions.Portraits) != 0 && (graphics->PortraitMode || graphics->PortraitPreview))
            || ((pauses & PauseOptions.IdleCamera) != 0 && GameMain.IsInIdleCam())
            || ((pauses & PauseOptions.Cutscenes) != 0 && !inGPose &&
                (_conditions[ConditionFlag.WatchingCutscene] || _conditions[ConditionFlag.WatchingCutscene78]
                    || _conditions[ConditionFlag.OccupiedInCutSceneEvent])))
        {
            _status = "Paused";
            return false;
        }

        return true;
    }

    private static bool TryGetFocus(Settings settings, out float distance)
    {
        distance = settings.FocusDistance;
        var cameras = CameraManager.Instance();
        if (cameras == null || (uint)cameras->CameraIndex >= cameras->Cameras.Length)
        {
            return false;
        }

        var camera = cameras->CurrentCamera;
        if (camera == null || camera->RenderCamera == null || camera->RenderCamera->IsOrtho)
        {
            return false;
        }

        var fov = camera->RenderCamera->FoV;
        if (!float.IsFinite(fov) || fov <= 0 || fov >= MathF.PI
            || !float.IsFinite(settings.FNumber) || settings.FNumber <= 0)
        {
            return false;
        }

        if (settings.AutoFocus)
        {
            distance = (((Experimental.CameraState*)camera)->Flags & 1) != 0
                ? Vector3.Distance(camera->Position, camera->LookAtVector)
                : 5f;
            distance = MathF.Max(distance, 0.5f);
        }

        var device = Device.Instance();

        // Calculation taken from DepthOfFieldCocLut_Update
        var focalLength = 0.012f / MathF.Tan(fov * 0.5f);
        var cocFactor = focalLength * focalLength / ((distance - focalLength) * settings.FNumber) * 45000f;
        return device != null && device->Width > 0 && float.IsFinite(distance) && distance > focalLength
            && float.IsFinite(focalLength) && focalLength > 0 && float.IsFinite(cocFactor) && cocFactor > 0;
    }
}
