using System;
using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace LiteShade.Graphics;

internal static unsafe class Experimental
{
    // TODO: Swap to FFXIVClientStructs when in main Dalamud.
    public const string RenderViewSignature = "E8 ?? ?? ?? ?? FF C5 49 83 C6 ?? BA";
    public const string RenderSignature = "40 53 57 41 54 41 55 48 83 EC ?? 65 48 8B 04 25";
    public const string CocVTableSignature = "48 89 6C 24 40 33 D2 B9 F8 00 00 00 E8 ?? ?? ?? ??";
    public const int CocVTableOffset = 0x81;
    public const string ResourcesSignature = "48 8B 05 ?? ?? ?? ?? 4C 8B 44 D0 10 4D 85 C0 74 ?? 49 83 B8 B0 00 00 00 00 74 ?? F6 81 98 00 00 00 08 74 ?? 80 B9 8E 00 00 00 00 76 ?? 48 83 79 38 00";
    public const string FilterVTableSignature = "48 89 6C 24 40 33 D2 B9 D0 00 00 00 E8 ?? ?? ?? ??";
    public const int FilterVTableOffset = 0x7E;
    public const string ManagerSignature = "48 8B 0D ?? ?? ?? ?? 8B 81 70 44 00 00 C1 E8 08 A8 01 0F 85";

    public delegate void RenderViewDelegate(Manager* manager, [MarshalAs(UnmanagedType.I1)] bool enabled, Manager.RenderViews view);
    public delegate void RenderDelegate(Manager* manager);
    public delegate void DrawFilterDelegate(FilterPart* part);

    public static Camera* GetMainCamera(Manager* manager)
        => manager->Views[(int)Manager.RenderViews.Main].SubViews[12].Camera;

    public static FilterPart* GetReadyPart(PostEffectManager* manager, nint drawAddress)
    {
        if (manager == null || manager == (PostEffectManager*)(-1)
            || manager->InitializedEffects != ulong.MaxValue
            || manager->FilterPartCount != 1 || manager->FilterParts == null
            || manager->FilterCommonBuffer == null || (manager->FilterEnabledMask & 1) == 0
            || manager->SceneInput == null || manager->SceneOutput == null)
        {
            return null;
        }

        var part = manager->FilterParts[0];
        if (part == null || part->VTable == null || part->VTable[5] != drawAddress
            || part->Lut0 == null || part->Lut1 == null || part->Lut2 == null
            || part->MatrixIndex >= part->ParameterCount || part->LutParameterIndex >= part->ParameterCount
            || part->DarkMatrixIndex >= part->ParameterCount || part->DarkParameterIndex >= part->ParameterCount
            || part->LutSamplerIndex >= part->SamplerCount)
        {
            return null;
        }

        return ((delegate* unmanaged<FilterPart*, byte>)part->VTable[4])(part) != 0 ? part : null;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x48C0)]
    public struct PostEffectManager
    {
        [FieldOffset(0x488)] public PostEffectChain DepthOfFieldChain;
        [FieldOffset(0x4C0)] public PostEffectChain DepthOfFieldCoCChain;
        [FieldOffset(0x4F8)] public PostEffectChain UpdatedDepthOfFieldChain;
        [FieldOffset(0x5E0)] public FilterPart** FilterParts;
        [FieldOffset(0x5E8)] public uint FilterPartCount;
        [FieldOffset(0x600)] public ConstantBuffer* FilterCommonBuffer;
        [FieldOffset(0x60C)] public uint FilterEnabledMask;
        [FieldOffset(0x4010)] public Texture* SceneInput;
        [FieldOffset(0x4030)] public Texture* Depth;
        [FieldOffset(0x4078)] public Texture* SceneOutput;
        [FieldOffset(0x40A0)] private Texture* Unk40A0;
        [FieldOffset(0x40A8)] private Texture* Unk40A8;
        [FieldOffset(0x40B0)] private Texture* Unk40B0;
        [FieldOffset(0x40B8)] private Texture* Unk40B8;
        [FieldOffset(0x40C8)] private Texture* Unk40C8;
        [FieldOffset(0x40D0)] private Texture* Unk40D0;
        [FieldOffset(0x4410)] public ulong InitializedEffects;
        [FieldOffset(0x4470)] public PostEffectFlags Flags;
        [FieldOffset(0x46E8)] public DepthOfFieldParameters DepthOfField;
        [FieldOffset(0x471C)] public Vector4 Curve;
        [FieldOffset(0x472C)] public FilterParameters Filter;

        public bool HasDepthOfFieldScratchTargets()
            => HasTexture(Unk40A0) && HasTexture(Unk40A8) && HasTexture(Unk40B0)
                && HasTexture(Unk40B8) && HasTexture(Unk40C8) && HasTexture(Unk40D0);
    }

    [Flags]
    public enum PostEffectFlags : uint
    {
        None = 0,
        AmbientOcclusion = 1 << 0,
        AntiAliasing = 1 << 1,
        Sky = 1 << 2,
        Moon = 1 << 3,
        SkyHaloRainbow = 1 << 4,
        Halo = 1 << 5,
        Fog = 1 << 6,
        DepthOfField = 1 << 7,
        CameraMotionBlur = 1 << 8,
        RadialBlur = 1 << 9,
        Glare = 1 << 10,
        GodRays = 1 << 11,
        LensFlare = 1 << 12,
        ToneMapping = 1 << 13,
        Saturate = 1 << 14,
        ColorFilterDarkBlend = 1 << 15,
        Vignetting = 1 << 16,
        ToneAdjust = 1 << 17,
        ChromaticAberration = 1 << 18,
        LetterBox = 1 << 19,
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x34)]
    public struct DepthOfFieldParameters
    {
        [FieldOffset(0x00)] public bool UseUpdatedDepthOfField;
        [FieldOffset(0x01)] public bool UseManualDepthOfField;
        [FieldOffset(0x02)] public bool UseFocusDistanceOverride;
        [FieldOffset(0x04)] public float NearFocusDistance;
        [FieldOffset(0x08)] public float FarFocusDistance;
        [FieldOffset(0x0C)] public float FarBlurDistance;
        [FieldOffset(0x10)] public float NearBlurRate;
        [FieldOffset(0x14)] public float FarBlurRate;
        [FieldOffset(0x1C)] public float EffectMaskRate;
        [FieldOffset(0x20)] public float FocusDistance;
        [FieldOffset(0x24)] public float FNumber;
        [FieldOffset(0x28)] public float CocNormalizationDivisor;
        [FieldOffset(0x2C)] public float PreviousFrameWeight;
        [FieldOffset(0x30)] public float PreviousRangeScale;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x38)]
    public struct PostEffectChain
    {
        [FieldOffset(0x08)] public PostEffectPart** Parts;
        [FieldOffset(0x10)] public uint PartCount;
        [FieldOffset(0x28)] public ConstantBuffer* CommonBuffer;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xA0)]
    public struct PostEffectPart
    {
        [FieldOffset(0x00)] public nint* VTable;
        [FieldOffset(0x08)] public ShaderCodeResourceHandle* PixelShader;
        [FieldOffset(0x10)] public void* ParameterData;
        [FieldOffset(0x18)] public void* ConstantBufferBindings;
        [FieldOffset(0x20)] public void* SamplerBindings;
        [FieldOffset(0x8C)] public byte ParameterCount;
        [FieldOffset(0x8D)] public byte SamplerCount;
        [FieldOffset(0x94)] public sbyte VertexShaderIndex;
        [FieldOffset(0x98)] public byte Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xD0)]
    public struct PostEffectResources
    {
        [FieldOffset(0x10)] public ShaderCodeResourceHandle* VertexShader0;
        [FieldOffset(0x90)] public void* VertexDeclaration;
        [FieldOffset(0x98)] public void* VertexBuffer;
        [FieldOffset(0xA8)] public ConstantBuffer* ParamBuffer;
        [FieldOffset(0xB8)] public ConstantBuffer* SamplingOffsetBuffer;
        [FieldOffset(0xC8)] public ConstantBuffer* DynamicViewportResolutionBuffer;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xF8)]
    public struct DepthOfFieldCocLut
    {
        [FieldOffset(0x00)] public PostEffectPart Part;
        [FieldOffset(0xA0)] public uint WeightParameterIndex;
        [FieldOffset(0xA4)] public uint CocParameterIndex;
        [FieldOffset(0xA8)] public uint LutSamplerIndex;
        [FieldOffset(0xB0)] public ConstantBuffer* CocParameterBuffer;
        [FieldOffset(0xB8)] public ConstantBuffer* DisabledCocParameterBuffer;
        [FieldOffset(0xF4)] public bool ResetHistory;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x740)]
    public struct DofRenderTargets
    {
        [FieldOffset(0x110)] public Texture* ViewPosition;
        [FieldOffset(0x258)] private Texture* Unk258;
        [FieldOffset(0x45E)] public byte CurrentSource;
        [FieldOffset(0x5F8)] public Texture* Depth0;
        [FieldOffset(0x600)] public Texture* Depth1;
        [FieldOffset(0x650)] public Texture* Velocity;
        [FieldOffset(0x668)] public Texture* Source0;
        [FieldOffset(0x670)] public Texture* Source1;
        [FieldOffset(0x678)] public Texture* CocLut;
        [FieldOffset(0x680)] public Texture* EffectMask;
        [FieldOffset(0x720)] public float ResolutionScaleX;
        [FieldOffset(0x724)] public float ResolutionScaleY;

        public bool HasDepthOfFieldTextures()
            => HasTexture(ViewPosition) && HasTexture(Unk258) && HasTexture(Velocity)
                && HasTexture(Depth0) && HasTexture(Depth1) && HasTexture(Source0) && HasTexture(Source1)
                && HasTexture(CocLut) && HasTexture(EffectMask);
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x38390)]
    public struct RenderManagerState
    {
        [FieldOffset(0x3834C)] public uint InitializationFlags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x100)]
    public struct CameraState
    {
        [FieldOffset(0xF0)] public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xD8)]
    public struct GraphicsConfigState
    {
        [FieldOffset(0x80)] private ulong PendingEventFlags;
        [FieldOffset(0x88)] private ulong EventFlags;

        public bool ResetTemporalHistory => (EventFlags & 1) != 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xE0AC8)]
    public struct DeviceState
    {
        [FieldOffset(0x7C)] public byte LastResolutionChangeRequest;
    }

    public static DepthOfFieldCocLut* GetReadyDepthOfField(PostEffectManager* manager, PostEffectResources* resources, nint* vtable)
    {
        var targets = (DofRenderTargets*)RenderTargetManager.Instance();
        var graphics = (GraphicsConfigState*)GraphicsConfig.Instance();
        var device = (DeviceState*)Device.Instance();
        if (manager == null || manager == (PostEffectManager*)(-1) || targets == null || graphics == null || device == null
            || resources == null || resources->VertexDeclaration == null || resources->VertexBuffer == null
            || !HasBuffer(resources->ParamBuffer, 0x30) || !HasBuffer(resources->SamplingOffsetBuffer, 0x100) || !HasBuffer(resources->DynamicViewportResolutionBuffer, 0x20)
            || graphics->ResetTemporalHistory || device->LastResolutionChangeRequest != 0
            || manager->InitializedEffects != ulong.MaxValue
            || !HasParts(manager->DepthOfFieldChain, 8, resources, false)
            || !HasParts(manager->DepthOfFieldCoCChain, 1, resources, true)
            || !HasParts(manager->UpdatedDepthOfFieldChain, 13, resources, true)
            || !HasBuffer(manager->DepthOfFieldCoCChain.CommonBuffer) || !HasBuffer(manager->UpdatedDepthOfFieldChain.CommonBuffer)
            || !HasTexture(manager->SceneInput) || !HasTexture(manager->SceneOutput) || !HasTexture(manager->Depth)
            || !manager->HasDepthOfFieldScratchTargets()
            || targets->CurrentSource > 1 || !targets->HasDepthOfFieldTextures())
        {
            return null;
        }

        if (!float.IsFinite(targets->ResolutionScaleX) || targets->ResolutionScaleX <= 0
            || !float.IsFinite(targets->ResolutionScaleY) || targets->ResolutionScaleY <= 0)
        {
            return null;
        }

        for (var index = 3; index <= 8; index++)
        {
            if (manager->UpdatedDepthOfFieldChain.Parts[index]->ParameterData == null)
            {
                return null;
            }
        }

        var coc = (DepthOfFieldCocLut*)manager->DepthOfFieldCoCChain.Parts[0];
        return coc->Part.VTable == vtable && coc->WeightParameterIndex < coc->Part.ParameterCount
            && coc->CocParameterIndex != uint.MaxValue && coc->LutSamplerIndex != uint.MaxValue
            && HasBuffer(coc->CocParameterBuffer) && HasBuffer(coc->DisabledCocParameterBuffer) ? coc : null;
    }

    private static bool HasTexture(Texture* texture)
        => texture != null && texture->AllocatedWidth > 0 && texture->AllocatedHeight > 0;

    private static bool HasBuffer(ConstantBuffer* buffer, int size = 16) => buffer != null && buffer->ByteSize >= size;

    private static bool HasParts(PostEffectChain chain, uint count, PostEffectResources* resources, bool resolved)
    {
        if (chain.Parts == null || chain.PartCount != count)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            var part = chain.Parts[index];
            if (part == null || part->VTable == null || (part->SamplerCount > 0 && part->SamplerBindings == null))
            {
                return false;
            }

            if (resolved && (part->PixelShader == null || part->PixelShader->Shader == null
                || part->VertexShaderIndex < 0 || part->VertexShaderIndex >= 16 || (part->Flags & 8) == 0
                || (part->ParameterCount > 0 && (part->ParameterData == null || part->ConstantBufferBindings == null))))
            {
                return false;
            }

            if (resolved)
            {
                var shader = (&resources->VertexShader0)[part->VertexShaderIndex];
                if (shader == null || shader->Shader == null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xD0)]
    public struct FilterPart
    {
        [FieldOffset(0)] public nint* VTable;
        [FieldOffset(0x8C)] public byte ParameterCount;
        [FieldOffset(0x8D)] public byte SamplerCount;
        [FieldOffset(0xA0)] public Texture* Lut0;
        [FieldOffset(0xA8)] public Texture* Lut1;
        [FieldOffset(0xB0)] public Texture* Lut2;
        [FieldOffset(0xBC)] public uint MatrixIndex;
        [FieldOffset(0xC0)] public uint LutParameterIndex;
        [FieldOffset(0xC4)] public uint LutSamplerIndex;
        [FieldOffset(0xC8)] public uint DarkMatrixIndex;
        [FieldOffset(0xCC)] public uint DarkParameterIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FilterParameters
    {
        public ColorMatrix Matrix;
        public ColorMatrix DarkMatrix;
        public Vector3 DarkParameters;
        public float Strength;
    }
}
