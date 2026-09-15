using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using LiteShade.Helpers;
using Lumina.Excel.Sheets;

namespace LiteShade.Windows;

internal sealed class ContextNames
{
    private Dictionary<uint, string>? _territories;
    private Dictionary<uint, string>? _areas;
    private Dictionary<uint, string>? _weather;

    public IReadOnlyDictionary<uint, string> Territories => _territories ??= NameChoices(IDataManager.Get().GetExcelSheet<TerritoryType>()
        .Where(row => row.RowId != 0 && row.PlaceName.IsValid)
        .Select(row => KeyValuePair.Create(row.RowId, row.PlaceName.Value.Name.ExtractText()))
        .Where(row => !string.IsNullOrWhiteSpace(row.Value))
        .OrderBy(row => row.Value));

    public IReadOnlyDictionary<uint, string> Areas => _areas ??= NameChoices(IDataManager.Get().GetExcelSheet<PlaceName>()
        .Where(row => row.RowId != 0)
        .Select(row => KeyValuePair.Create(row.RowId, row.Name.ExtractText()))
        .Where(row => !string.IsNullOrWhiteSpace(row.Value))
        .OrderBy(row => row.Value));

    public IReadOnlyDictionary<uint, string> Weathers => _weather ??= NameChoices(IDataManager.Get().GetExcelSheet<Weather>()
        .Where(row => row.RowId is > 0 and <= byte.MaxValue)
        .Select(row => KeyValuePair.Create(row.RowId, row.Name.ExtractText()))
        .Where(row => !string.IsNullOrWhiteSpace(row.Value))
        .OrderBy(row => row.Key));

    public string Territory(uint id) => Territories.GetValueOrDefault(id) ?? $"Unknown zone ({id})";
    public string Area(uint id) => Areas.GetValueOrDefault(id) ?? $"Unknown area ({id})";
    public string Weather(uint id) => Weathers.GetValueOrDefault(id) ?? $"Unknown weather ({id})";

    private static Dictionary<uint, string> NameChoices(IEnumerable<KeyValuePair<uint, string>> rows)
    {
        return rows.ToDictionary(row => row.Key, row => $"{row.Value} ({row.Key})");
    }
}
