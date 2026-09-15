using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using LiteShade.Helpers;
using FilterRow = Lumina.Excel.Sheets.ColorFilter;

namespace LiteShade.Graphics;

internal sealed record GameFilter(uint Id, string Name, ColorMatrix Matrix, Vector4 Curve)
{
    public static unsafe GameFilter[] Load()
    {
        // TODO: Swap to new Lumina names when merged and in main Dalamud
        var buildMatrix = (delegate* unmanaged<Experimental.EnvColorFilterParameters*, Matrix4x4*, void>)
            ISigScanner.Get().ScanText(Experimental.BuildFilterMatrixSignature);
        var rows = IDataManager.Get().GetExcelSheet<FilterRow>()
            .Where(row => row.RowId != 0 && row.Unknown14 != 0)
            .OrderBy(row => row.Unknown14).ThenBy(row => row.RowId).ToArray();
        var filters = new GameFilter[rows.Length];

        byte* storage = stackalloc byte[0x4F];
        var output = (Matrix4x4*)(((nuint)storage + 15) & ~(nuint)15);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var parameters = new Experimental.EnvColorFilterParameters
            {
                Curve = new Vector4(row.Unknown12, row.Unknown13, row.Unknown15 ? 2 * row.Unknown3 : 0, row.Unknown15 ? row.Unknown4 : 0),
                Hue = row.Unknown1,
                Saturation = row.Unknown2,
                Brightness = row.Unknown15 ? 0 : row.Unknown3,
                Contrast = row.Unknown15 ? 0 : row.Unknown4,
                TintColor = new Vector3(row.Unknown8, row.Unknown9, row.Unknown10),
                TintStrength = row.Unknown7,
                Sepia = row.Unknown6,
                Monochrome = row.Unknown5,
                Invert = row.Unknown11,
                Strength = 1,
            };
            buildMatrix(&parameters, output);
            var matrix = new ColorMatrix(
                new Vector4(output->M11, output->M21, output->M31, output->M41),
                new Vector4(output->M12, output->M22, output->M32, output->M42),
                new Vector4(output->M13, output->M23, output->M33, output->M43));
            if (!matrix.IsFinite || !float.IsFinite(parameters.Curve.X) || !float.IsFinite(parameters.Curve.Y)
                || !float.IsFinite(parameters.Curve.Z) || !float.IsFinite(parameters.Curve.W))
            {
                throw new InvalidOperationException($"Invalid game filter ({row.RowId}).");
            }

            filters[index] = new GameFilter(row.RowId, row.Unknown0.ToString(), matrix, parameters.Curve);
        }

        return filters;
    }
}
