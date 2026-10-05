using System;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;

namespace LiteShade.Graphics;

internal static unsafe class Experimental
{
    public const string TextureMapSignature = "E8 ?? ?? ?? ?? 4C 8B D8 48 39 7D";
    public const string TextureUnmapSignature = "E8 ?? ?? ?? ?? 0F B6 5C 24 ?? EB";

    public delegate void* TextureMapDelegate(Texture* texture, uint mipLevel, TextureMapResult* result);
    public delegate void TextureUnmapDelegate(Texture* texture, uint mipLevel);

    [StructLayout(LayoutKind.Explicit, Size = 0x10)]
    public struct TextureMapResult
    {
        [FieldOffset(0x00)] public uint RowPitch;
        [FieldOffset(0x04)] private uint Unk04;
        [FieldOffset(0x08)] public void* Data;
    }

    public static PostEffectColorFilterDarkBlend* GetReadyPart(PostEffectManager* manager,
        PostEffectColorFilterDarkBlend.PostEffectColorFilterDarkBlendVirtualTable* vtable)
    {
        if (manager == null || manager == (PostEffectManager*)(-1)
            || manager->InitializedEffects != ulong.MaxValue
            || manager->ColorFilterChain.PartCount != 1 || manager->ColorFilterChain.Parts == null
            || manager->ColorFilterChain.CommonBuffer == null || (manager->ColorFilterChain.EnabledPartMask & 1) == 0
            || manager->SceneInput == null || manager->SceneOutput == null)
        {
            return null;
        }

        var part = (PostEffectColorFilterDarkBlend*)manager->ColorFilterChain.Parts[0];
        if (part == null || part->VirtualTable != vtable
            || part->LutTextures[0].Value == null || part->LutTextures[1].Value == null || part->LutTextures[2].Value == null
            || part->MatrixParameterIndex >= part->ParameterCount || part->LutParameterIndex >= part->ParameterCount
            || part->DarkMatrixParameterIndex >= part->ParameterCount || part->DarkParameterIndex >= part->ParameterCount
            || part->LutSamplerIndex >= part->SamplerCount)
        {
            return null;
        }

        return part->IsValid() ? part : null;
    }

    public static PostEffectDepthOfFieldCocLut* GetReadyDepthOfField(PostEffectManager* manager, PostEffectResources* resources,
        PostEffectDepthOfFieldCocLut.PostEffectDepthOfFieldCocLutVirtualTable* vtable)
    {
        var targets = RenderTargetManager.Instance();
        var graphics = GraphicsConfig.Instance();
        var device = Device.Instance();
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
            || !((ScratchTargets*)manager)->HasTextures()
            || targets->DepthOfFieldSourceIndex > 1 || !HasDepthOfFieldTextures(targets))
        {
            return null;
        }

        if (!float.IsFinite(targets->GraphicsRezoScale) || targets->GraphicsRezoScale <= 0
            || !float.IsFinite(targets->GraphicsRezoScaleY) || targets->GraphicsRezoScaleY <= 0)
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

        var coc = (PostEffectDepthOfFieldCocLut*)manager->DepthOfFieldCoCChain.Parts[0];
        return coc->VirtualTable == vtable && coc->WeightParameterIndex < coc->ParameterCount
            && coc->CocParameterIndex != uint.MaxValue && coc->LutSamplerIndex != uint.MaxValue
            && HasBuffer(coc->CocParameterBuffer) && HasBuffer(coc->DisabledCocParameterBuffer) ? coc : null;
    }

    public static bool IsVignetteReady(PostEffectManager* manager, PostEffectResources* resources)
    {
        var targets = RenderTargetManager.Instance();
        if (manager == null || manager == (PostEffectManager*)(-1) || manager->InitializedEffects != ulong.MaxValue
            || targets == null || targets->Resolution_Width == 0 || targets->Resolution_Height == 0
            || resources == null || resources->VertexDeclaration == null || resources->VertexBuffer == null
            || !HasBuffer(resources->ParamBuffer, 0x30) || !HasBuffer(resources->SamplingOffsetBuffer, 0x100)
            || !HasBuffer(resources->DynamicViewportResolutionBuffer, 0x20)
            || !HasParts(manager->VignettingChain, 2, resources, true)
            || !HasBuffer(manager->VignettingChain.CommonBuffer) || (manager->VignettingChain.EnabledPartMask & 3) != 3)
        {
            return false;
        }

        var parts = manager->VignettingChain.Parts;
        return parts[0]->ParameterCount >= 1 && parts[1]->ParameterCount >= 1 && parts[1]->SamplerCount >= 1
            && HasTexture(parts[0]->OutputTextures[0].Value) && parts[1]->InputTexture == parts[0]->OutputTextures[0].Value;
    }

    private static bool HasDepthOfFieldTextures(RenderTargetManager* targets)
        => HasTexture(targets->ViewPosition) && HasTexture(((RenderTargetState*)targets)->Unk258) && HasTexture(targets->Velocity)
            && HasTexture(targets->DepthHistory0) && HasTexture(targets->DepthHistory1)
            && HasTexture(targets->DepthOfFieldSource0) && HasTexture(targets->DepthOfFieldSource1)
            && HasTexture(targets->DepthOfFieldCocLut) && HasTexture(targets->DepthOfFieldEffectMask);

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
            if (part == null || part->VirtualTable == null || (part->SamplerCount > 0 && part->SamplerBindings == null))
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
                var shader = resources->VertexShaders[part->VertexShaderIndex].Value;
                if (shader == null || shader->Shader == null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // These textures aren't public in FFXIVClientStructs.
    [StructLayout(LayoutKind.Explicit, Size = 0x40D8)]
    private struct ScratchTargets
    {
        [FieldOffset(0x40A0)] private Texture* Unk40A0;
        [FieldOffset(0x40A8)] private Texture* Unk40A8;
        [FieldOffset(0x40B0)] private Texture* Unk40B0;
        [FieldOffset(0x40B8)] private Texture* Unk40B8;
        [FieldOffset(0x40C8)] private Texture* Unk40C8;
        [FieldOffset(0x40D0)] private Texture* Unk40D0;

        public bool HasTextures()
            => HasTexture(Unk40A0) && HasTexture(Unk40A8) && HasTexture(Unk40B0)
                && HasTexture(Unk40B8) && HasTexture(Unk40C8) && HasTexture(Unk40D0);
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x260)]
    private struct RenderTargetState
    {
        [FieldOffset(0x258)] internal Texture* Unk258;
    }
}
