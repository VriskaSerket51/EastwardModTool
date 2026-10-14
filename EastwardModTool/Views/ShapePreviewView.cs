using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

public sealed class ShapePreviewView : UserControl
{
    public ShapePreviewView(LegacyDeckAsset asset)
    {
        var values = asset.Data!["polyline"]!.AsArray().Select(value => double.Parse(value!.ToString(), CultureInfo.InvariantCulture)).ToArray();
        var points = values.Chunk(2).Where(pair => pair.Length == 2).Select(pair => new Point(pair[0], -pair[1])).ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var width = Math.Max(1, points.Max(point => point.X) - left);
        var height = Math.Max(1, points.Max(point => point.Y) - top);
        var scale = Math.Min(600 / width, 450 / height);
        var canvas = new Canvas { Name = "ShapeCanvas", Width = 650, Height = 500 };
        canvas.Children.Add(new Polyline { Points = new Avalonia.Collections.AvaloniaList<Point>(points.Append(points[0]).Select(point => new Point(25 + (point.X - left) * scale, 25 + (point.Y - top) * scale))), Stroke = Brush.Parse("#78B9B1"), StrokeThickness = 2 });
        Content = new TabControl { Items = { new TabItem { Header = "Outline", Content = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } }, new TabItem { Header = "Definition", Content = new JsonDataView(() => asset.Data) } } };
    }
}
