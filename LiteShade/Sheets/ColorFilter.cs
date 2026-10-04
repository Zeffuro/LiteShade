using Lumina.Excel;
using Lumina.Text.ReadOnly;

namespace LiteShade.Sheets;

// Swap to Lumina when in mainline Dalamud
[Sheet("ColorFilter", 0x7F9DB367)]
public readonly struct ColorFilter(ExcelPage page, uint offset, uint row) : IExcelRow<ColorFilter>
{
    public ExcelPage ExcelPage => page;
    public uint RowOffset => offset;
    public uint RowId => row;

    public ReadOnlySeString Name => page.ReadString(offset, offset);
    public float Hue => page.ReadFloat32(offset + 4);
    public float Saturation => page.ReadFloat32(offset + 8);
    public float Brightness => page.ReadFloat32(offset + 12);
    public float Contrast => page.ReadFloat32(offset + 16);
    public float MonochromeStrength => page.ReadFloat32(offset + 20);
    public float SepiaStrength => page.ReadFloat32(offset + 24);
    public float TintStrength => page.ReadFloat32(offset + 28);
    public float TintRed => page.ReadFloat32(offset + 32);
    public float TintGreen => page.ReadFloat32(offset + 36);
    public float TintBlue => page.ReadFloat32(offset + 40);
    public float InvertStrength => page.ReadFloat32(offset + 44);
    public float ToneCurveLowThreshold => page.ReadFloat32(offset + 48);
    public float ToneCurveHighThreshold => page.ReadFloat32(offset + 52);
    public byte SortOrder => page.ReadUInt8(offset + 56);
    public bool UseNonlinearBrightnessContrast => page.ReadPackedBool(offset + 57, 0);

    static ColorFilter IExcelRow<ColorFilter>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
}
