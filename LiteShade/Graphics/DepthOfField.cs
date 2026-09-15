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
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using RenderCamera = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Camera;

namespace LiteShade.Graphics;

internal sealed unsafe class DepthOfField : IDisposable
{
    public readonly record struct Settings(FocusMode Focus, float FocusDistance, float FNumber, PauseOptions Pauses);

    private readonly ProfileService _profiles;

    private readonly IFramework _framework = IFramework.Get();
    private readonly IClientState _client = IClientState.Get();
    private readonly ICondition _conditions = ICondition.Get();
    private readonly ITargetManager _targets = ITargetManager.Get();

    // TODO: Swap to FFXIVClientStructs when in main Dalamud.
    private readonly Hook<Experimental.RenderDelegate>? _hook;
    private readonly Experimental.PostEffectManager** _manager;
    private readonly Experimental.PostEffectResources** _resources;

    private readonly nint* _cocVTable;
    private volatile string _status = "Disabled";
    private volatile string _focusDescription = string.Empty;
    private volatile float _focusDistance;
    private ulong? _focusTargetId;
    private string _targetDescription = string.Empty;

    public string Status => _status;
    public string FocusDescription => _focusDescription;
    public float EffectiveFocusDistance => _focusDistance;

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

        var (_, settings, _) = _profiles.RenderSettings;
        if (settings is not { } dof)
        {
            _status = "Disabled";
            _focusDescription = string.Empty;
            _focusDistance = 0;
            _focusTargetId = null;
            _hook!.Original(renderManager);
            return;
        }

        if (!CanApply(renderManager, dof.Pauses))
        {
            _focusDescription = string.Empty;
            _focusDistance = 0;
            _focusTargetId = null;
            _hook!.Original(renderManager);
            return;
        }

        var manager = *_manager;
        var coc = Experimental.GetReadyDepthOfField(manager, *_resources, _cocVTable);
        if (coc == null || !TryGetFocus(dof, Experimental.GetMainCamera(renderManager), out var distance, out var focusDescription)
            || !float.IsFinite(manager->DepthOfField.CocNormalizationDivisor) || manager->DepthOfField.CocNormalizationDivisor <= 0
            || !float.IsFinite(1f / manager->DepthOfField.CocNormalizationDivisor))
        {
            _status = "Waiting for depth of field resources";
            _focusDescription = string.Empty;
            _focusDistance = 0;
            _focusTargetId = null;
            _hook!.Original(renderManager);
            return;
        }

        var original = manager->DepthOfField;
        var nativeEnabled = (manager->Flags & Experimental.PostEffectFlags.DepthOfField) != 0;
        _focusDescription = focusDescription;
        _focusDistance = distance;

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

    private bool TryGetFocus(Settings settings, RenderCamera* renderCamera, out float distance, out string description)
    {
        distance = settings.FocusDistance;
        description = "Fixed distance";
        ulong? focusTargetId = null;
        var cameras = CameraManager.Instance();
        if (cameras == null || (uint)cameras->CameraIndex >= cameras->Cameras.Length)
        {
            return false;
        }

        var camera = cameras->CurrentCamera;
        if (camera == null || renderCamera == null || camera->RenderCamera != renderCamera || renderCamera->IsOrtho)
        {
            return false;
        }

        var fov = renderCamera->FoV;
        if (!float.IsFinite(fov) || fov <= 0 || fov >= MathF.PI
            || !float.IsFinite(settings.FNumber) || settings.FNumber <= 0)
        {
            return false;
        }

        if (settings.Focus != FocusMode.Manual)
        {
            var hasLookAt = (((Experimental.CameraState*)camera)->Flags & 1) != 0;
            distance = hasLookAt ? -Vector4.Transform(new Vector4(camera->LookAtVector, 1f), renderCamera->ViewMatrix).Z : 5f;
            if (!float.IsFinite(distance) || distance <= 0)
            {
                hasLookAt = false;
                distance = 5f;
            }

            distance = MathF.Max(distance, 0.5f);
            description = hasLookAt ? "Camera look-at" : "Camera fallback (5 yalms)";

            if (settings.Focus == FocusMode.Target)
            {
                description = hasLookAt ? "Camera look-at (target unavailable)" : "Camera fallback (5 yalms, target unavailable)";
                var target = GameMain.IsInGPose() ? _targets.GPoseTarget : _targets.Target;
                if (target != null && target.IsValid())
                {
                    Vector3 center = default;
                    ((GameObject*)target.Address)->GetCenterPosition(&center);

                    var depth = -Vector4.Transform(new Vector4(center, 1f), renderCamera->ViewMatrix).Z;
                    if (float.IsFinite(depth) && depth > 0)
                    {
                        distance = MathF.Max(depth, 0.5f);
                        focusTargetId = target.GameObjectId;
                        if (_focusTargetId != focusTargetId)
                        {
                            var name = target.Name.TextValue;
                            _targetDescription = string.IsNullOrWhiteSpace(name) ? "Target" : $"Target: {name}";
                        }

                        description = _targetDescription;
                    }
                }
            }
        }

        _focusTargetId = focusTargetId;
        var device = Device.Instance();

        // Calculation taken from DepthOfFieldCocLut_Update
        var focalLength = 0.012f / MathF.Tan(fov * 0.5f);
        var cocFactor = focalLength * focalLength / ((distance - focalLength) * settings.FNumber) * 45000f;
        return device != null && device->Width > 0 && float.IsFinite(distance) && distance > focalLength
            && float.IsFinite(focalLength) && focalLength > 0 && float.IsFinite(cocFactor) && cocFactor > 0;
    }
}
