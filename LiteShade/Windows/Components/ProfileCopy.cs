using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class ProfileCopy(ProfilesTab editor)
{
    private Guid _sourceId;
    private Guid _targetId;
    private Effect _effects;
    private bool _open;

    public void Open(ColorProfile target)
    {
        _targetId = target.Id;
        _sourceId = editor.Config.Profiles.First(profile => profile.Id != target.Id).Id;
        _effects = Effect.ColourAdjustments;
        _open = true;
        ImGui.OpenPopup("Copy effects");
    }

    public bool Draw()
    {
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Copy effects", ref _open, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup) return false;

        var target = editor.Config.Profiles.FirstOrDefault(profile => profile.Id == _targetId);
        var source = editor.Config.Profiles.FirstOrDefault(profile => profile.Id == _sourceId);
        if (target is null || source is null)
        {
            _open = false;
            ImGui.CloseCurrentPopup();
            return false;
        }

        if (editor.Picker.Draw("From", ref _sourceId, target.Id))
        {
            source = editor.Config.Profiles.First(profile => profile.Id == _sourceId);
        }

        ImGui.TextDisabled($"To: {target.Name}");
        foreach (var effect in Effects.All)
        {
            var selected = (_effects & effect) != 0;
            if (ImGui.Checkbox(effect.Label(), ref selected))
            {
                _effects = selected ? _effects | effect : _effects & ~effect;
            }
        }

        bool apply;
        using (ImRaii.Disabled(_effects == 0)) apply = ImGui.Button("Copy");
        if (apply)
        {
            editor.Config.Profiles[editor.Config.Profiles.IndexOf(target)] = ProfileEffects.Copy(source, target, _effects);
            editor.Save();
            _open = false;
            ImGui.CloseCurrentPopup();
            return true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _open = false;
            ImGui.CloseCurrentPopup();
        }

        return false;
    }
}
