using System.Globalization;
using Avalonia.Controls;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.App;

public static class LightInspector
{
    public static Control Build(Light light, EditHistory history)
    {
        var p = InspectorPanel.Panel($"Light {light.Num}");

        p.Children.Add(InspectorPanel.Row("Flags",
            InspectorPanel.TextField($"0x{light.Flags:x}",
                text =>
                {
                    var style = NumberStyles.HexNumber;
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        style = NumberStyles.AllowHexSpecifier;
                        text = text[2..];
                    }
                    if (long.TryParse(text, style, CultureInfo.InvariantCulture, out var flags))
                        history.Do(new SetLightFlagsCommand(light, flags));
                })));

        p.Children.Add(InspectorPanel.Row("Range",
            InspectorPanel.NumericField(light.Range,
                v => history.Do(new SetLightRangeCommand(light, v)))));

        p.Children.Add(InspectorPanel.Row("Intensity",
            InspectorPanel.NumericField(light.Intensity,
                v => history.Do(new SetLightIntensityCommand(light, v)))));

        p.Children.Add(InspectorPanel.Row("Color R",
            InspectorPanel.NumericField(light.Color.R,
                v => history.Do(new SetLightColorCommand(light, new ColorF((float)v, light.Color.G, light.Color.B))))));
        p.Children.Add(InspectorPanel.Row("Color G",
            InspectorPanel.NumericField(light.Color.G,
                v => history.Do(new SetLightColorCommand(light, new ColorF(light.Color.R, (float)v, light.Color.B))))));
        p.Children.Add(InspectorPanel.Row("Color B",
            InspectorPanel.NumericField(light.Color.B,
                v => history.Do(new SetLightColorCommand(light, new ColorF(light.Color.R, light.Color.G, (float)v))))));

        p.Children.Add(InspectorPanel.Row("X",
            InspectorPanel.NumericField(light.Position.X,
                v => history.Do(new SetLightPositionCommand(light, new Vec3(v, light.Position.Y, light.Position.Z))))));
        p.Children.Add(InspectorPanel.Row("Y",
            InspectorPanel.NumericField(light.Position.Y,
                v => history.Do(new SetLightPositionCommand(light, new Vec3(light.Position.X, v, light.Position.Z))))));
        p.Children.Add(InspectorPanel.Row("Z",
            InspectorPanel.NumericField(light.Position.Z,
                v => history.Do(new SetLightPositionCommand(light, new Vec3(light.Position.X, light.Position.Y, v))))));

        p.Children.Add(InspectorPanel.Row("Layer",
            InspectorPanel.NumericField(light.Layer,
                v => history.Do(new SetLightLayerCommand(light, (int)v)))));

        return p;
    }
}
