using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class GraphPreviewView : UserControl, IDisposable
{
    private readonly TextDocument _document;
    public GraphPreviewView(TextDocument document)
    {
        _document = document;
        document.PropertyChanged += Changed;
        Loaded += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        try
        {
            var xml = XDocument.Parse(_document.Text);
            var nodes = xml.Descendants().Where(element => element.Name.LocalName == "node").ToArray();
            var canvas = new Canvas { Name = "GraphCanvas" };
            var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
            var minimumX = nodes.Select(node => Number(node.Descendants().FirstOrDefault(item => item.Name.LocalName == "Geometry")?.Attribute("x"))).DefaultIfEmpty(0).Min();
            var minimumY = nodes.Select(node => Number(node.Descendants().FirstOrDefault(item => item.Name.LocalName == "Geometry")?.Attribute("y"))).DefaultIfEmpty(0).Min();
            var width = 0.0;
            var height = 0.0;
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                var geometry = node.Descendants().FirstOrDefault(item => item.Name.LocalName == "Geometry");
                var x = geometry == null ? index % 4 * 200 + 24 : Number(geometry.Attribute("x")) - minimumX + 24;
                var y = geometry == null ? index / 4 * 100 + 24 : Number(geometry.Attribute("y")) - minimumY + 24;
                positions[(string)node.Attribute("id")!] = new Point(x + 80, y + 25);
                var label = node.Descendants().FirstOrDefault(item => item.Name.LocalName is "NodeLabel" or "label")?.Value ?? (string?)node.Attribute("id") ?? "Node";
                var box = new Border { Background = Brush.Parse("#33485C"), BorderBrush = Brush.Parse("#78B9B1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Width = 160, MinHeight = 50, Padding = new Thickness(8), Child = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } };
                ToolTip.SetTip(box, node.ToString());
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                canvas.Children.Add(box);
                width = Math.Max(width, x + 180);
                height = Math.Max(height, y + 80);
            }
            foreach (var edge in xml.Descendants().Where(element => element.Name.LocalName == "edge"))
            {
                if (!positions.TryGetValue((string)edge.Attribute("source")!, out var from) || !positions.TryGetValue((string)edge.Attribute("target")!, out var to))
                {
                    continue;
                }

                var direction = new Vector(to.X - from.X, to.Y - from.Y);
                if (direction.Length < 1)
                {
                    continue;
                }

                var trim = 1 / Math.Max(Math.Abs(direction.X) / 80, Math.Abs(direction.Y) / 25);
                var end = to - direction * Math.Min(0.45, trim);
                canvas.Children.Insert(0, new Line { StartPoint = from, EndPoint = end, Stroke = Brushes.Gray, StrokeThickness = 1 });
                var directed = (string?)edge.Attribute("directed") != "false" && ((string?)edge.Attribute("directed") == "true" || (string?)edge.Parent?.Attribute("edgedefault") == "directed");
                if (directed)
                {
                    var unit = direction / direction.Length;
                    var side = new Vector(-unit.Y, unit.X);
                    canvas.Children.Insert(0, new Polygon { Points = new Avalonia.Collections.AvaloniaList<Point> { end, end - unit * 10 + side * 4, end - unit * 10 - side * 4 }, Fill = Brushes.Gray });
                }
            }
            canvas.Width = Math.Max(400, width);
            canvas.Height = Math.Max(300, height);
            Content = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        }
        catch (Exception error) when (error is System.Xml.XmlException or ArgumentException or FormatException or InvalidOperationException) { Content = new TextBlock { Text = "Graph source cannot be previewed: " + error.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) }; }
    }
    private static double Number(XAttribute? value)
    {
        return value == null ? 0 : double.Parse(value.Value, CultureInfo.InvariantCulture);
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TextDocument.Text) && TopLevel.GetTopLevel(this) != null && IsEffectivelyVisible)
        {
            Refresh();
        }
    }
    public void Dispose()
    {
        _document.PropertyChanged -= Changed;
    }
}
