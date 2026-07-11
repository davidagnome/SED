using System.Globalization;
using Avalonia.Controls;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.App;

public static class SectorInspector
{
    public static Control Build(Sector sector, EditHistory history)
    {
        var p = InspectorPanel.Panel($"Sector {sector.Num}");

        p.Children.Add(InspectorPanel.Row("Flags", InspectorPanel.TextField(
            $"0x{sector.Flags:x}", text =>
            {
                if (long.TryParse(text.TrimStart("0x".ToCharArray()),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags))
                    history.Do(new SetSectorFlagsCommand(sector, flags));
            })));

        p.Children.Add(InspectorPanel.Row("Ambient", InspectorPanel.NumericField(
            sector.Ambient.R, v =>
                history.Do(new SetSectorAmbientCommand(sector, new ColorF((float)v, (float)v, (float)v))))));

        p.Children.Add(InspectorPanel.Row("Extra Light", InspectorPanel.NumericField(
            sector.ExtraLight.R, v =>
                history.Do(new SetSectorExtraLightCommand(sector, new ColorF((float)v, (float)v, (float)v))))));

        p.Children.Add(InspectorPanel.Row("Tint R", InspectorPanel.NumericField(
            sector.Tint.R, v =>
                history.Do(new SetSectorTintCommand(sector, new ColorF((float)v, sector.Tint.G, sector.Tint.B))))));

        p.Children.Add(InspectorPanel.Row("Tint G", InspectorPanel.NumericField(
            sector.Tint.G, v =>
                history.Do(new SetSectorTintCommand(sector, new ColorF(sector.Tint.R, (float)v, sector.Tint.B))))));

        p.Children.Add(InspectorPanel.Row("Tint B", InspectorPanel.NumericField(
            sector.Tint.B, v =>
                history.Do(new SetSectorTintCommand(sector, new ColorF(sector.Tint.R, sector.Tint.G, (float)v))))));

        p.Children.Add(InspectorPanel.Row("Colormap", InspectorPanel.TextField(
            sector.ColorMap, text => history.Do(new SetSectorColormapCommand(sector, text)))));

        p.Children.Add(InspectorPanel.Row("Sound", InspectorPanel.TextField(
            sector.Sound, text => history.Do(new SetSectorSoundCommand(sector, text, sector.SoundVolume)))));

        p.Children.Add(InspectorPanel.Row("Sound Vol", InspectorPanel.NumericField(
            sector.SoundVolume, v => history.Do(new SetSectorSoundCommand(sector, sector.Sound, v)))));

        p.Children.Add(InspectorPanel.Row("Layer", InspectorPanel.NumericField(
            sector.Layer, v => history.Do(new SetSectorLayerCommand(sector, (int)v)))));

        return p;
    }
}
