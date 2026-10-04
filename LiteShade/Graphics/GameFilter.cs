using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using LiteShade.Helpers;
using FilterRow = LiteShade.Sheets.ColorFilter;
using Matrix4x4 = FFXIVClientStructs.FFXIV.Common.Math.Matrix4x4;

namespace LiteShade.Graphics;

internal sealed record GameFilter(uint Id, string Name, ColorMatrix Matrix, Vector4 Curve)
{
    public static unsafe GameFilter[] Load()
    {
        var rows = IDataManager.Get().GetExcelSheet<FilterRow>()
            .Where(row => row.RowId != 0 && row.SortOrder != 0)
            .OrderBy(row => row.SortOrder).ThenBy(row => row.RowId).ToArray();
        var filters = new GameFilter[rows.Length];

        byte* storage = stackalloc byte[0x4F];
        nuint alignmentMask = 15;
        var output = (Matrix4x4*)(((nuint)storage + alignmentMask) & ~alignmentMask);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var parameters = new EnvColorFilterParameters
            {
                Curve = new Vector4(row.ToneCurveLowThreshold, row.ToneCurveHighThreshold,
                    row.UseNonlinearBrightnessContrast ? 2 * row.Brightness : 0,
                    row.UseNonlinearBrightnessContrast ? row.Contrast : 0),
                Hue = row.Hue,
                Saturation = row.Saturation,
                Brightness = row.UseNonlinearBrightnessContrast ? 0 : row.Brightness,
                Contrast = row.UseNonlinearBrightnessContrast ? 0 : row.Contrast,
                TintColor = new Vector3(row.TintRed, row.TintGreen, row.TintBlue),
                TintStrength = row.TintStrength,
                Sepia = row.SepiaStrength,
                Monochrome = row.MonochromeStrength,
                Invert = row.InvertStrength,
                Strength = 1,
            };
            parameters.BuildMatrix(output);
            var matrix = new ColorMatrix(
                new Vector4(output->M11, output->M21, output->M31, output->M41),
                new Vector4(output->M12, output->M22, output->M32, output->M42),
                new Vector4(output->M13, output->M23, output->M33, output->M43));
            if (!matrix.IsFinite || !float.IsFinite(parameters.Curve.X) || !float.IsFinite(parameters.Curve.Y)
                || !float.IsFinite(parameters.Curve.Z) || !float.IsFinite(parameters.Curve.W))
            {
                throw new InvalidOperationException($"Invalid game filter ({row.RowId}).");
            }

            filters[index] = new GameFilter(row.RowId, row.Name.ToString(), matrix, parameters.Curve);
        }

        return filters;
    }
}
