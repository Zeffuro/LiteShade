#if DEBUG
using Dalamud.Bindings.ImGui;
using LiteShade.Graphics;
using LiteShade.Integrations;
using LiteShade.Profiles;

namespace LiteShade.Windows.Tabs;

internal sealed class DiagnosticsTab
{
    private readonly ProfileService _profiles;
    private readonly ColorFilter _filter;
    private readonly ContextNames _names;
    public bool ReShadeLoaded { get; private set; } = ReShadeDetector.IsLoaded();

    public DiagnosticsTab(ProfileService profiles, ColorFilter filter, ContextNames names)
    {
        _profiles = profiles;
        _filter = filter;
        _names = names;
    }

    public void Draw()
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
        ImGui.TextUnformatted($"ReShade loaded: {ReShadeLoaded}");
        if (ImGui.Button("Rescan loaded modules"))
        {
            ReShadeLoaded = ReShadeDetector.IsLoaded();
        }
    }
}
#endif
