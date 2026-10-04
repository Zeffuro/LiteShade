using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using LiteShade.Configuration;
using LiteShade.Configuration.Sharing;
using LiteShade.Helpers;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class ProfileSharing
{
    private readonly ProfilesTab _editor;
    private readonly FileDialogManager _files = new();
    private readonly ProfileImportWindow _import;
    private readonly PackDetails _pack;
    private SharedSetupSnapshot? _beforeImport;
    private string? _fileImport;
    private bool _reset;
    private bool _undoImport;
    private string? _message;
    private bool _failed;

    public ProfileSharing(ProfilesTab editor)
    {
        _editor = editor;
        _import = new ProfileImportWindow(editor, backup =>
        {
            _beforeImport = backup;
            Success("Profiles imported.");
        });
        _pack = new PackDetails(editor);
    }

    public void DrawDialogs() => _files.Draw();

    public void CloseDialogs()
    {
        _files.Reset();
        _fileImport = null;
        _import.Cancel();
    }

    public bool Draw(ColorProfile profile)
    {
        if (_fileImport is { } text)
        {
            _fileImport = null;
            OpenImport(text);
        }

        ImGui.SameLine();
        if (ImGui.Button("Actions...")) ImGui.OpenPopup("Profile actions");
        var import = false;
        var reset = false;
        var undo = false;
        var copy = false;
        var pack = false;
        using (var popup = ImRaii.Popup("Profile actions"))
        {
            if (popup)
            {
                copy = ImGui.MenuItem("Copy effects from...", string.Empty, false, _editor.Config.Profiles.Count > 1);
                ImGui.Separator();
                if (ImGui.MenuItem("Copy profile")) Export(profile.Id, false);
                if (ImGui.MenuItem("Copy profile + conditions")) Export(profile.Id, true);
                if (ImGui.MenuItem("Copy entire pack")) Export(null, true);
                pack = ImGui.MenuItem("Pack details...");
                ImGui.Separator();
                import = ImGui.MenuItem("Import from clipboard...");
                undo = ImGui.MenuItem("Undo last import...", string.Empty, false, _beforeImport is not null);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Available until LiteShade reloads.");
                using (var menu = ImRaii.Menu("Files"))
                {
                    if (menu)
                    {
                        if (ImGui.MenuItem("Import file...")) ImportFile();
                        if (ImGui.MenuItem("Save profile...")) Export(profile.Id, true, true);
                        if (ImGui.MenuItem("Save entire pack...")) Export(null, true, true);
                    }
                }

                ImGui.Separator();
                reset = ImGui.MenuItem("Reset all settings...");
            }
        }

        if (copy) _editor.OpenCopyEffects(profile);
        if (pack) _pack.Open();
        if (import) OpenImport(ImGui.GetClipboardText());
        if (reset)
        {
            _reset = true;
            ImGui.OpenPopup("Reset LiteShade");
        }

        if (undo)
        {
            _undoImport = true;
            ImGui.OpenPopup("Undo import");
        }

        if (_message is { } message)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, _failed ? ImGuiColors.DalamudRed : ImGuiColors.HealerGreen);
            ImGui.TextWrapped(message);
        }

        _pack.Draw();
        return _import.Draw() || DrawUndoImport() || DrawReset();
    }

    private void OpenImport(string text)
    {
        try
        {
            _import.Open(text);
            _message = null;
        }
        catch (FormatException exception)
        {
            TransferError(exception);
        }
    }

    private void Export(Guid? profileId, bool includeRules, bool file = false)
    {
        try
        {
            _editor.Save();
            var text = ProfileTransfer.Export(_editor.Config, profileId, includeRules);
            if (!file)
            {
                ImGui.SetClipboardText(text);
                Success("Copied to clipboard.");
                return;
            }

            _files.SaveFileDialog("Save LiteShade pack", ".liteshade", "LiteShade", ".liteshade", (success, path) =>
            {
                if (!success) return;
                try
                {
                    File.WriteAllText(path, text);
                    Success("Pack saved.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    TransferError(exception);
                }
            });
        }
        catch (Exception exception)
        {
            TransferError(exception);
        }
    }

    private void ImportFile()
    {
        _files.OpenFileDialog("Import LiteShade pack", ".liteshade,.txt", (success, path) =>
        {
            if (!success) return;
            try
            {
                using var file = File.OpenRead(path);
                if (file.Length > 1024 * 1024) throw new FormatException("Profile data is too large.");
                using var reader = new StreamReader(file);
                _fileImport = reader.ReadToEnd();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                TransferError(exception);
            }
        });
    }

    private bool DrawUndoImport()
    {
        using var popup = ImRaii.PopupModal("Undo import", ref _undoImport, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup) return false;
        ProfileImportWindow.Warning("Restore your profiles and conditions from before the last import?");
        ImGui.TextDisabled("Changes made since that import will also be undone.");
        using (ImRaii.Disabled(_beforeImport is null))
        {
            if (ImGui.Button("Restore"))
            {
                _beforeImport!.Restore(_editor.Config);
                _beforeImport = null;
                _editor.Imported(_editor.Config.DefaultProfileId, true);
                _editor.Save();
                Success("Import undone.");
                _undoImport = false;
                ImGui.CloseCurrentPopup();
                return true;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _undoImport = false;
            ImGui.CloseCurrentPopup();
        }

        return false;
    }

    private bool DrawReset()
    {
        var width = 420f * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        using var popup = ImRaii.PopupModal("Reset LiteShade", ref _reset, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup) return false;
        ProfileImportWindow.Warning("Remove all profiles and conditions and start again?");
        if (ImGui.Button("Reset"))
        {
            _message = null;
            _beforeImport = null;
            _import.Cancel();
            _editor.Reset();
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

    private void Success(string message)
    {
        _failed = false;
        _message = message;
    }

    private void TransferError(Exception exception)
    {
        _failed = true;
        _message = exception is FormatException ? exception.Message : "Could not transfer profiles. Check the Dalamud log.";
        if (exception is not FormatException) IPluginLog.Get().Error(exception, "Could not transfer profiles.");
    }
}
