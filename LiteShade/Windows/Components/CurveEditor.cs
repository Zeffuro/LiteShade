using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LiteShade.Configuration;
using LiteShade.Graphics;
using LiteShade.Windows.Tabs;

namespace LiteShade.Windows.Components;

internal sealed class CurveEditor(ProfilesTab editor)
{
    private readonly float[] _samples = new float[256];
    private ToneCurve? _sampled;
    private bool _changed;

    public void Flush(bool force = false)
    {
        if (!_changed || (!force && ImGui.IsMouseDown(ImGuiMouseButton.Left))) return;
        _changed = false;
        editor.Save();
    }

    public void Draw(ColorProfile profile)
    {
        using var indent = ImRaii.PushStyle(ImGuiStyleVar.IndentSpacing, 0f);
        using var node = ImRaii.TreeNode("RGB curve");
        if (!node) return;

        if (ImGui.SmallButton("Reset"))
        {
            _changed = false;
            profile.Curve = ToneCurve.Identity;
            editor.Save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Drag to move, click to add, right-click to remove.");

        using var plot = ImRaii.Plot("##RGB curve", new Vector2(-1, 230 * ImGuiHelpers.GlobalScale),
            ImPlotFlags.NoTitle | ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText);
        if (!plot) return;
        var axisFlags = ImPlotAxisFlags.Lock | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoLabel;
        ImPlot.SetupAxes("", "", axisFlags, axisFlags);
        ImPlot.SetupAxisLimits(ImAxis.X1, -0.04, 1.04, ImPlotCond.Always);
        ImPlot.SetupAxisLimits(ImAxis.Y1, -0.04, 1.04, ImPlotCond.Always);
        if (_sampled != profile.Curve)
        {
            ColorCurve.Sample(profile.Curve, _samples);
            _sampled = profile.Curve;
        }

        ImPlot.SetNextLineStyle(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], 2);
        ImPlot.PlotLine("Curve", ref _samples[0], _samples.Length, 1d / 255d);
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.SliderGrabActive];
        var disabled = (ImGui.GetCurrentContext().CurrentItemFlags & ImGuiItemFlags.Disabled) != 0;
        var mouse = ImGui.GetMousePos();
        var hitRadius = 8 * ImGuiHelpers.GlobalScale;
        var nearest = -1;
        var nearestDistance = hitRadius * hitRadius;
        for (var i = 0; i < profile.Curve.Points.Length; i++)
        {
            var point = profile.Curve.Points[i];
            var distance = Vector2.DistanceSquared(mouse, ImPlot.PlotToPixels(point.X, point.Y));
            if (distance <= nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        var plotPosition = ImPlot.GetPlotPos();
        var plotHovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(plotPosition, plotPosition + ImPlot.GetPlotSize());
        if (!disabled && plotHovered && !_changed)
        {
            var updated = profile.Curve;
            var adding = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && nearest < 0;
            if (adding)
            {
                var point = ImPlot.GetPlotMousePos();
                updated = updated.AddPoint(new Vector2((float)point.X, (float)point.Y));
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && nearest >= 0)
            {
                updated = updated.RemovePoint(nearest);
            }

            if (updated != profile.Curve)
            {
                profile.Curve = updated;
                if (adding)
                {
                    _changed = true;
                    editor.Profiles.Refresh();
                    editor.MarkPreviewDirty();
                }
                else editor.Save();
            }
        }

        for (var i = 0; i < profile.Curve.Points.Length; i++)
        {
            var point = profile.Curve.Points[i];
            double x = point.X;
            double y = point.Y;
            var flags = ImPlotDragToolFlags.NoFit;
            if (disabled) flags |= ImPlotDragToolFlags.NoInputs;
            if (!ImPlot.DragPoint(i, ref x, ref y, color, 5 * ImGuiHelpers.GlobalScale, flags)) continue;
            var curve = profile.Curve.WithPoint(i, new Vector2((float)x, (float)y));
            if (!curve.IsValid || curve == profile.Curve) continue;
            profile.Curve = curve;
            _changed = true;
            editor.Profiles.Refresh();
            editor.MarkPreviewDirty();
        }
    }
}
