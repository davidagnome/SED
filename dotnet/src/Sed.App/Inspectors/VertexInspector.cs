using Avalonia.Controls;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.App;

public static class VertexInspector
{
    public static Control Build(Vertex vertex, EditHistory history)
    {
        var p = InspectorPanel.Panel("Vertex");

        p.Children.Add(InspectorPanel.Row("X", InspectorPanel.NumericField(
            vertex.Position.X,
            v => history.Do(new SetVertexPositionCommand(vertex, new Vec3(v, vertex.Position.Y, vertex.Position.Z))))));

        p.Children.Add(InspectorPanel.Row("Y", InspectorPanel.NumericField(
            vertex.Position.Y,
            v => history.Do(new SetVertexPositionCommand(vertex, new Vec3(vertex.Position.X, v, vertex.Position.Z))))));

        p.Children.Add(InspectorPanel.Row("Z", InspectorPanel.NumericField(
            vertex.Position.Z,
            v => history.Do(new SetVertexPositionCommand(vertex, new Vec3(vertex.Position.X, vertex.Position.Y, v))))));

        p.Children.Add(InspectorPanel.Row("Sector", new TextBlock
        {
            Text = vertex.Sector?.Num.ToString() ?? "?",
            FontSize = 11,
        }));

        return p;
    }
}
