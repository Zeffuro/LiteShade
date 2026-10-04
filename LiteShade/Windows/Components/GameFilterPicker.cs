using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Graphics;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class GameFilterPicker
{
    private readonly ProfilesTab _editor;
    private string _filterSearch = string.Empty;

    public GameFilterPicker(ProfilesTab editor) => _editor = editor;

    public void DrawControl(ColorProfile profile)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Filter");
        ImGui.TableNextColumn();
        var preview = FilterName(profile.GameFilterId);
        var arrowWidth = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(Math.Max(100 * ImGuiHelpers.GlobalScale,
            ImGui.GetContentRegionAvail().X - 2 * (arrowWidth + ImGui.GetStyle().ItemSpacing.X)));
        using (var combo = ImRaii.Combo("##Game filter", preview))
        {
            if (combo)
            {
                DrawFilterSearch();
                DrawGameFilterOptions(profile);
            }
        }

        ImGui.SameLine();
        DrawFilterArrows(profile);
    }

    public void DrawPicker(ColorProfile profile)
    {
        using var popup = ImRaii.Popup("GPose filters", ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            return;
        }

        ImGui.TextUnformatted($"Profile: {profile.Name}");
        ImGui.TextDisabled(_editor.IsPreviewing ? "Trying this profile" :
            _editor.Config.Enabled && _editor.Profiles.Selection.ProfileId == profile.Id ? "Edits apply live" : "Editing an inactive profile");
        ImGui.Separator();
        DrawFilterSearch();
        DrawFilterArrows(profile);
        ImGui.SameLine();
        ImGui.TextUnformatted(FilterName(profile.GameFilterId));
        var appearing = ImGui.IsWindowAppearing();
        using (var child = ImRaii.Child("Filter list", ImGuiHelpers.ScaledVector2(320f, 280f)))
        {
            if (child)
            {
                DrawGameFilterOptions(profile, ImGuiSelectableFlags.DontClosePopups, appearing);
            }
        }

        if (ImGui.Button("Done"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawGameFilterOptions(ColorProfile profile, ImGuiSelectableFlags flags = ImGuiSelectableFlags.None,
        bool scrollToSelected = false)
    {
        if (ImGui.Selectable("None", profile.GameFilterId == 0, flags))
        {
            profile.GameFilterId = 0;
            _editor.Save();
        }

        if (scrollToSelected && profile.GameFilterId == 0)
        {
            ImGui.SetScrollHereY();
        }

        var choices = FilterChoices();
        foreach (var filter in choices)
        {
            using var id = ImRaii.PushId((int)filter.Id);
            var favorite = _editor.Config.FavoriteGameFilters.Contains(filter.Id);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)(favorite ? ImGuiCol.CheckMark : ImGuiCol.TextDisabled)]))
            {
                if (ImGui.SmallButton(FontAwesomeIcon.Star.ToIconString()))
                {
                    if (favorite)
                    {
                        _editor.Config.FavoriteGameFilters.Remove(filter.Id);
                    }
                    else
                    {
                        _editor.Config.FavoriteGameFilters.Add(filter.Id);
                    }

                    _editor.Save();
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(favorite ? "Remove favourite" : "Favourite");
            }

            ImGui.SameLine();
            if (ImGui.Selectable($"{filter.Name} ({filter.Id})", filter.Id == profile.GameFilterId, flags))
            {
                profile.GameFilterId = filter.Id;
                _editor.Save();
            }

            if (scrollToSelected && filter.Id == profile.GameFilterId)
            {
                ImGui.SetScrollHereY();
            }
        }

        if (choices.Length == 0 && !string.IsNullOrWhiteSpace(_filterSearch))
        {
            ImGui.TextDisabled("No matching filters.");
        }

        if (_editor.Filter.GameFiltersError is { } error)
        {
            ImGui.TextDisabled(error);
        }
    }

    private void DrawFilterSearch()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##Filter search", "Search name or ID", ref _filterSearch, 80);
    }

    private void DrawFilterArrows(ColorProfile profile)
    {
        if (ImGui.ArrowButton("Previous filter", ImGuiDir.Left))
        {
            CycleGameFilter(profile, -1);
        }

        ImGui.SameLine();
        if (ImGui.ArrowButton("Next filter", ImGuiDir.Right))
        {
            CycleGameFilter(profile, 1);
        }
    }

    private string FilterName(uint id)
    {
        var filter = _editor.Filter.GameFilters.FirstOrDefault(item => item.Id == id);
        return filter is null ? id == 0 ? "None" : $"Unavailable ({id})" : $"{filter.Name} ({id})";
    }

    private GameFilter[] FilterChoices()
    {
        var search = _filterSearch.Trim();
        return _editor.Filter.GameFilters.Where(filter => search.Length == 0
                || filter.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || filter.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(filter => _editor.Config.FavoriteGameFilters.Contains(filter.Id)).ToArray();
    }

    private void CycleGameFilter(ColorProfile profile, int direction)
    {
        var choices = FilterChoices();
        var current = Array.FindIndex(choices, filter => filter.Id == profile.GameFilterId) + 1;
        var next = (current + direction + choices.Length + 1) % (choices.Length + 1);
        profile.GameFilterId = next == 0 ? 0 : choices[next - 1].Id;
        _editor.Save();
    }
}
