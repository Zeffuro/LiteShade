using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;

namespace LiteShade.Windows.Components;

internal sealed class ProfilePicker
{
    private readonly SystemConfiguration _config;
    private readonly Action _save;

    public ProfilePicker(SystemConfiguration config, Action save)
    {
        _config = config;
        _save = save;
    }

    public bool Draw(string label, ref Guid selected, Guid? exclude = null)
    {
        var selectedId = selected;
        var current = _config.Profiles.FirstOrDefault(profile => profile.Id == selectedId);
        using var combo = ImRaii.Combo(label, current?.Name ?? "Missing profile");
        if (!combo) return false;

        var profiles = _config.Profiles.Where(profile => profile.Id != exclude).ToArray();
        if (!profiles.Any(profile => profile.Folder.Length > 0))
        {
            foreach (var profile in profiles.OrderByDescending(profile => _config.FavoriteProfiles.Contains(profile.Id)))
            {
                if (DrawProfile(profile, ref selected)) return true;
            }

            return false;
        }

        var favourites = profiles.Where(profile => _config.FavoriteProfiles.Contains(profile.Id)).ToArray();
        var remaining = profiles.Where(profile => !_config.FavoriteProfiles.Contains(profile.Id)).ToArray();
        if (favourites.Length > 0)
        {
            ImGui.TextDisabled("Favourites");
            foreach (var profile in favourites)
            {
                if (DrawProfile(profile, ref selected)) return true;
            }

            ImGui.Separator();
        }

        foreach (var profile in remaining.Where(profile => profile.Folder.Length == 0))
        {
            if (DrawProfile(profile, ref selected)) return true;
        }

        foreach (var folder in remaining.Where(profile => profile.Folder.Length > 0)
                     .GroupBy(profile => profile.Folder, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            using var node = ImRaii.TreeNode(folder.Key, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
            if (!node) continue;

            foreach (var profile in folder)
            {
                if (DrawProfile(profile, ref selected)) return true;
            }
        }

        return false;
    }

    private bool DrawProfile(ColorProfile profile, ref Guid selected)
    {
        using var id = ImRaii.PushId(profile.Id.ToString());
        DrawFavourite(profile);
        ImGui.SameLine();
        if (!ImGui.Selectable(profile.Name, profile.Id == selected)) return false;
        selected = profile.Id;
        return true;
    }

    public void DrawFavourite(ColorProfile profile)
    {
        var favourite = _config.FavoriteProfiles.Contains(profile.Id);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)(favourite ? ImGuiCol.CheckMark : ImGuiCol.TextDisabled)]))
        {
            if (ImGui.SmallButton(FontAwesomeIcon.Star.ToIconString()))
            {
                if (favourite) _config.FavoriteProfiles.Remove(profile.Id);
                else _config.FavoriteProfiles.Add(profile.Id);
                _save();
            }
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(favourite ? "Remove favourite" : "Favourite");
    }
}
