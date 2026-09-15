using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using LiteShade.Configuration;
using LiteShade.Configuration.Sharing;
using LiteShade.Helpers;

namespace LiteShade.Windows;

internal sealed partial class ConfigWindow
{
    private ProfileExport? _import;
    private bool _importRules;
    private bool _enableImportedRules;
    private bool _replaceProfiles;
    private bool _reset;
    private string? _transferMessage;
    private bool _transferFailed;

    private bool DrawTransfer(ColorProfile profile)
    {
        ImGui.SameLine();
        if (ImGui.Button("Export..."))
        {
            ImGui.OpenPopup("Export profiles");
        }

        using (var popup = ImRaii.Popup("Export profiles"))
        {
            if (popup)
            {
                ImGui.TextDisabled(profile.Name);
                if (ImGui.MenuItem("This profile"))
                {
                    Export(profile.Id, false);
                }

                if (ImGui.MenuItem("This profile + conditions"))
                {
                    Export(profile.Id, true);
                }

                ImGui.Separator();
                if (ImGui.MenuItem("All profiles + conditions"))
                {
                    Export(null, true);
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Import..."))
        {
            try
            {
                _import = ProfileTransfer.Parse(ImGui.GetClipboardText());
                _importRules = _import.Rules.Count > 0;
                _enableImportedRules = false;
                _replaceProfiles = false;
                _transferMessage = null;
                ImGui.OpenPopup("Import profiles");
            }
            catch (Exception exception)
            {
                TransferError(exception);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset all..."))
        {
            _reset = true;
            ImGui.OpenPopup("Reset LiteShade");
        }

        if (_transferMessage is { } message)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, _transferFailed ? ImGuiColors.DalamudRed : ImGuiColors.HealerGreen);
            ImGui.TextWrapped(message);
        }

        return DrawImport() || DrawReset();
    }

    private bool DrawImport()
    {
        var open = _import is not null;
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Import profiles", ref open, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup || _import is null)
        {
            if (!open)
            {
                _import = null;
            }

            return false;
        }

        ImGui.TextUnformatted($"Profiles: {_import.Profiles.Count}   Conditions: {_import.Rules.Count}");
        foreach (var profile in _import.Profiles.Take(6))
        {
            ImGui.TextWrapped(profile.Name);
        }

        if (_import.Profiles.Count > 6)
        {
            ImGui.TextDisabled($"+ {_import.Profiles.Count - 6} more");
        }

        ImGui.Spacing();
        ImGui.Checkbox("Replace all profiles and conditions", ref _replaceProfiles);

        if (_replaceProfiles)
        {
            Warning("Your existing profiles and conditions will be removed.");
            var defaultProfile = _import.Profiles.First(profile => profile.Id == _import.DefaultProfileId);
            ImGui.TextWrapped($"Default profile: {defaultProfile.Name}");
        }

        if (_import.Rules.Count > 0)
        {
            ImGui.Checkbox("Include conditions", ref _importRules);
            using (ImRaii.Disabled(!_importRules))
            {
                ImGui.Checkbox("Enable imported conditions", ref _enableImportedRules);
            }

            if (!_replaceProfiles)
            {
                ImGui.TextDisabled("Appended after your existing rules.");
            }
            else
            {
                var automatic = _importRules && _enableImportedRules && _import.AutomaticProfiles;
                ImGui.TextDisabled($"Automatic selection: {(automatic ? "on" : "off")}");
            }
        }

        if (ImGui.Button(_replaceProfiles ? "Replace and import" : "Import"))
        {
            _editingProfileId = ProfileTransfer.Import(_config, _import, _importRules, _enableImportedRules, _replaceProfiles);
            _confirmDelete = false;
            if (_replaceProfiles)
            {
                _editingRuleId = null;
                PluginState.WelcomeWindow.IsOpen = false;
                _profiles.SetPreview(null);
            }

            Save();
            _transferFailed = false;
            _transferMessage = "Profiles imported.";
            _import = null;
            ImGui.CloseCurrentPopup();
            return true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _import = null;
            ImGui.CloseCurrentPopup();
        }

        return false;
    }

    private void Export(Guid? profileId, bool includeRules)
    {
        try
        {
            ImGui.SetClipboardText(ProfileTransfer.Export(_config, profileId, includeRules));
            _transferFailed = false;
            _transferMessage = "Copied to clipboard.";
        }
        catch (Exception exception)
        {
            TransferError(exception);
        }
    }

    private bool DrawReset()
    {
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Reset LiteShade", ref _reset, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            return false;
        }

        Warning("Remove all profiles and conditions and start again?");
        ImGui.TextWrapped("Colour adjustments will be disabled. You can pick a new starting look.");
        if (ImGui.Button("Reset"))
        {
            _config.Reset();
            _editingProfileId = _config.DefaultProfileId;
            _editingRuleId = null;
            _confirmDelete = false;
            _advanced = false;
            _transferMessage = null;
            _import = null;
            Save();
            PluginState.WelcomeWindow.Show();
            _reset = false;
            ImGui.CloseCurrentPopup();
            return true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _reset = false;
            ImGui.CloseCurrentPopup();
        }

        return false;
    }

    private static void Warning(string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationCircle.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private void TransferError(Exception exception)
    {
        _transferFailed = true;
        _transferMessage = exception is FormatException ? exception.Message : "Could not transfer profiles. Check the Dalamud log.";

        if (exception is not FormatException)
        {
            IPluginLog.Get().Error(exception, "Could not transfer profiles.");
        }
    }
}
