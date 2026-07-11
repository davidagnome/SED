using System.Globalization;
using Avalonia.Controls;
using Sed.Core.Editing;
using Sed.Core.Model;

namespace Sed.App;

public static class SurfaceInspector
{
    public static Control Build(Surface surface, EditHistory history)
    {
        var p = InspectorPanel.Panel($"Surface {surface.Num} (sector {surface.Sector.Num})");

        p.Children.Add(InspectorPanel.Row("Surf Flags",
            InspectorPanel.TextField($"0x{surface.SurfFlags:x}", text =>
            {
                if (TryParseHex(text, out long v))
                    history.Do(new SetSurfaceFlagsCommand(surface, v));
            })));

        p.Children.Add(InspectorPanel.Row("Face Flags",
            InspectorPanel.TextField($"0x{surface.FaceFlags:x}", text =>
            {
                if (TryParseHex(text, out long v))
                    history.Do(new SetFaceFlagsCommand(surface, v));
            })));

        p.Children.Add(InspectorPanel.Row("Material",
            InspectorPanel.TextField(surface.Material, text =>
                history.Do(new SetMaterialCommand(surface, text, surface.MaterialIndex)))));

        p.Children.Add(InspectorPanel.Row("Geo",
            InspectorPanel.TextField(surface.Geo.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    history.Do(new SetSurfaceGeoCommand(surface, v));
            })));

        p.Children.Add(InspectorPanel.Row("Light",
            InspectorPanel.TextField(surface.Light.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    history.Do(new SetSurfaceLightModeCommand(surface, v));
            })));

        p.Children.Add(InspectorPanel.Row("Tex",
            InspectorPanel.TextField(surface.Tex.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    history.Do(new SetSurfaceTexModeCommand(surface, v));
            })));

        p.Children.Add(InspectorPanel.Row("Extra Light",
            InspectorPanel.NumericField(surface.ExtraLightIntensity, v =>
                history.Do(new SetSurfaceExtraLightCommand(surface, (float)v)))));

        p.Children.Add(InspectorPanel.Row("U Scale",
            InspectorPanel.NumericField(surface.UScale, v =>
                history.Do(new SetSurfaceScaleCommand(surface, (float)v, surface.VScale)))));

        p.Children.Add(InspectorPanel.Row("V Scale",
            InspectorPanel.NumericField(surface.VScale, v =>
                history.Do(new SetSurfaceScaleCommand(surface, surface.UScale, (float)v)))));

        if (surface.Adjoin is not null)
        {
            p.Children.Add(InspectorPanel.Row("Adjoin Flags",
                InspectorPanel.TextField($"0x{surface.AdjoinFlags:x}", text =>
                {
                    if (TryParseHex(text, out long v))
                        history.Do(new SetSurfaceAdjoinFlagsCommand(surface, v));
                })));
        }

        return p;
    }

    private static bool TryParseHex(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return long.TryParse(text, NumberStyles.HexNumber | NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
