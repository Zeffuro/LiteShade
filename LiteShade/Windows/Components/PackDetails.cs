using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class PackDetails(ProfilesTab editor)
{
    private ProfilePack? _pack;

    public void Open()
    {
        _pack = editor.Config.Pack.Copy();
        ImGui.OpenPopup("Pack details");
    }

    public void Draw()
    {
        var open = _pack is not null;
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Pack details", ref open, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup || _pack is null)
        {
            if (!open) _pack = null;
            return;
        }

        var name = _pack.Name;
        if (ImGui.InputText("Name", ref name, 81)) _pack.Name = name;
        var author = _pack.Author;
        if (ImGui.InputTextWithHint("Author", "Optional", ref author, 81)) _pack.Author = author;
        var revision = _pack.Revision;
        if (ImGui.InputInt("Revision", ref revision)) _pack.Revision = Math.Max(1, revision);
        ImGui.TextDisabled("Raise the revision when sharing an updated pack.");
        if (ImGui.Button("Save"))
        {
            editor.Config.Pack = _pack;
            editor.Save();
            _pack = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _pack = null;
            ImGui.CloseCurrentPopup();
        }
    }
}
