using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

public sealed class MeshPreviewView : UserControl
{
    private readonly MeshAsset _mesh;
    private readonly ComboBox _projection = new() { Name = "MeshProjection", ItemsSource = new[] { "XY", "XZ", "YZ" }, SelectedIndex = 0, Margin = new Thickness(8) };
    public MeshPreviewView(MeshAsset mesh)
    {
        _mesh = mesh;
        _projection.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        var source = ((TextAsset)_mesh.Items["output.obj"]).Text.Split('\n');
        var positions = source.Where(line => line.StartsWith("v ", StringComparison.Ordinal)).Select(line => line[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        var faces = source.Where(line => line.StartsWith("f ", StringComparison.Ordinal)).Select(line => line[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value.Split('/')[0], CultureInfo.InvariantCulture) - 1).ToArray()).ToArray();
        var canvas = new Canvas { Name = "MeshCanvas", Width = 700, Height = 500 };
        var horizontal = _projection.SelectedIndex == 2 ? 1 : 0;
        var vertical = _projection.SelectedIndex == 0 ? 1 : 2;
        if (positions.Length > 0)
        {
            var left = positions.Min(point => point[horizontal]);
            var right = positions.Max(point => point[horizontal]);
            var top = positions.Min(point => point[vertical]);
            var bottom = positions.Max(point => point[vertical]);
            var scale = Math.Min(650 / Math.Max(1, right - left), 450 / Math.Max(1, bottom - top));
            Point Project(int index)
            {
                return new Point(25 + (positions[index][horizontal] - left) * scale, 475 - (positions[index][vertical] - top) * scale);
            }
            foreach (var indices in faces)
            {
                for (var index = 0; index < indices.Length; index++)
                {
                    canvas.Children.Add(new Line { StartPoint = Project(indices[index]), EndPoint = Project(indices[(index + 1) % indices.Length]), Stroke = Brush.Parse("#78B9B1"), StrokeThickness = 1 });
                }
            }
        }
        var count = faces.Length;
        Content = new DockPanel { Children = { _projection, new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } };
        DockPanel.SetDock(_projection, Dock.Top);
        ToolTip.SetTip(canvas, $"{count} faces · wireframe projection of stored vertices");
    }
}
