using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect;
using LiteShade.Helpers;

namespace LiteShade.Graphics;

internal sealed unsafe class ColorLut : IDisposable
{
    private readonly Hook<PostEffectColorFilterDarkBlend.Delegates.UpdateLut>? _hook;
    private readonly Experimental.TextureMapDelegate? _map;
    private readonly Experimental.TextureUnmapDelegate? _unmap;
    private readonly byte[] _bytes = new byte[256];
    private PostEffectManager* _manager;
    private PostEffectColorFilterDarkBlend* _part;
    private ColorCurve? _curve;
    private ColorCurve? _from;
    private float _blend;
    private float _strength;
    private Vector4 _native;
    private bool _cached;
    private bool _failed;

    public string? Error { get; private set; }

    public ColorLut()
    {
        var scanner = ISigScanner.Get();
        if (!scanner.TryScanText(Experimental.TextureMapSignature, out var map)
            || !scanner.TryScanText(Experimental.TextureUnmapSignature, out var unmap)) return;

        _map = Marshal.GetDelegateForFunctionPointer<Experimental.TextureMapDelegate>(map);
        _unmap = Marshal.GetDelegateForFunctionPointer<Experimental.TextureUnmapDelegate>(unmap);
        _hook = IGameInteropProvider.Get().HookFromAddress<PostEffectColorFilterDarkBlend.Delegates.UpdateLut>(
            PostEffectColorFilterDarkBlend.MemberFunctionPointers.UpdateLut, UpdateLut);
    }

    public void Enable() => _hook?.Enable();

    public void Begin(PostEffectManager* manager, PostEffectColorFilterDarkBlend* part, ColorCurve curve, ColorCurve? from, float blend, float strength)
    {
        Error = null;
        if (strength == 0 || (curve.IsIdentity && from is not { IsIdentity: false })) return;
        if (_hook is null || _failed)
        {
            Error = "Curves unavailable";
            return;
        }

        var native = (Vector4)manager->ColorFilterCurve;
        if (!_cached || _curve != curve || _from != from || _blend != blend || _strength != strength || _native != native)
        {
            _cached = ColorCurve.BuildLut(curve, from, blend, strength, native, _bytes);
            if (!_cached)
            {
                Error = "Curve out of range";
                return;
            }
            _curve = curve;
            _from = from;
            _blend = blend;
            _strength = strength;
            _native = native;
        }

        _manager = manager;
        _part = part;
    }

    public void End()
    {
        _manager = null;
        _part = null;
    }

    public void Dispose() => _hook?.Dispose();

    private void UpdateLut(PostEffectColorFilterDarkBlend* part)
    {
        if (!IFramework.Get().IsInFrameworkUpdateThread || part != _part || _manager == null || PostEffectManager.Instance() != _manager)
        {
            _hook!.Original(part);
            return;
        }

        var index = (part->LutIndex + 1) % 3;
        var texture = part->LutTextures[(int)index].Value;
        if (part->LutIndex >= 3 || !IsCurveTexture(texture))
        {
            Error = "Curve texture unavailable";
            _hook!.Original(part);
            return;
        }

        part->LutIndex = index;
        var mapped = new Experimental.TextureMapResult();
        _map!(texture, 0, &mapped);
        if (mapped.Data == null)
        {
            Error = "Curve upload failed";
            return;
        }

        try
        {
            if (mapped.RowPitch != 256)
            {
                _failed = true;
                Error = "Curve texture unavailable";
                return;
            }

            _bytes.CopyTo(new Span<byte>(mapped.Data, 256));
        }
        finally
        {
            _unmap!(texture, 0);
        }
    }

    private static bool IsCurveTexture(Texture* texture)
        => texture != null && texture->D3D11Texture2D != null && texture->ActualWidth == 256 && texture->ActualHeight == 1
            && texture->MipLevel == 1 && texture->TextureFormat == TextureFormat.A8_UNORM
            && (texture->Flags & TextureFlags.TextureTypeMask) == TextureFlags.TextureType2D
            && (texture->Flags & (TextureFlags.Immutable | TextureFlags.CpuRead)) == 0
            && ((uint)texture->Flags & 0xA000) == 0x2000;
}
