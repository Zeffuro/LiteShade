#if DEBUG
using Dalamud.Bindings.ImGui;
using LiteShade.Integrations;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private void DrawDiagnostics()
    {
        var context = _profiles.Context;
        ImGui.TextUnformatted($"Rendering: {_filter.Status}");
        ImGui.TextUnformatted($"Depth of field: {PluginState.DepthOfField?.Status ?? "Unavailable"}");
        ImGui.TextUnformatted($"Vignette: {PluginState.Vignette?.Status ?? "Unavailable"}");
        ImGui.Separator();
        ImGui.TextUnformatted($"Logged in: {context.IsLoggedIn} | Loading: {context.IsTransitioning}");
        ImGui.TextUnformatted($"Territory: {_names.Territory(context.TerritoryId)}");
        ImGui.TextUnformatted($"Area PlaceName: {(context.AreaId is { } area ? _names.Area(area) : "Unavailable")}");
        ImGui.TextUnformatted($"Active weather: {(context.WeatherId is { } weather ? _names.Weather(weather) : "Unavailable")}");
        ImGui.TextUnformatted($"Eorzea time: {(context.DayTimeSeconds is { } seconds ? System.TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : "Unavailable")}");
        ImGui.TextUnformatted($"In duty: {context.InDuty} | In combat: {context.InCombat}");
        ImGui.Separator();
        ImGui.TextUnformatted($"ReShade loaded: {_reShadeLoaded}");
        if (ImGui.Button("Rescan loaded modules"))
        {
            _reShadeLoaded = ReShadeDetector.IsLoaded();
        }
    }
}
#endif
