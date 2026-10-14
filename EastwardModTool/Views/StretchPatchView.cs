using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

/// <summary>Shows the original texture and the fixed/stretching region boundaries.</summary>
public sealed class StretchPatchView : UserControl, IDisposable
{
    private readonly Bitmap _bitmap;
    public StretchPatchView(LegacyDeckAsset asset)
    {
        var texture = asset.Texture!;
        using var pixels = new MemoryStream(texture.ToPng());
        _bitmap = new Bitmap(pixels);
        var canvas = new Canvas { Name = "StretchPatchCanvas", Width = texture.Width, Height = texture.Height };
        var image = new Image { Source = _bitmap, Width = texture.Width, Height = texture.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        canvas.Children.Add(image);
        double Fraction(string key) => asset.Data?[key] == null ? 0.33 : double.Parse(asset.Data[key]!.ToString(), CultureInfo.InvariantCulture);
        bool Flag(string key, bool fallback) => asset.Data?[key] == null ? fallback : asset.Data[key]!.GetValue<bool>();
        void Guide(Point from, Point to) => canvas.Children.Add(new Line { StartPoint = from, EndPoint = to, Stroke = Brush.Parse("#F3AE48"), StrokeThickness = 1 });
        if (Flag("splitX", true))
        {
            foreach (var x in new[] { Fraction("left") * texture.Width, (1 - Fraction("right")) * texture.Width })
            {
                Guide(new Point(x, 0), new Point(x, texture.Height));
            }
        }
        if (Flag("splitY", true))
        {
            foreach (var y in new[] { Fraction("top") * texture.Height, (1 - Fraction("bottom")) * texture.Height })
            {
                Guide(new Point(0, y), new Point(texture.Width, y));
            }
        }
        var zoom = new ComboBox { ItemsSource = new[] { "1×", "2×", "4×", "8×" }, SelectedIndex = 0, Margin = new Thickness(8) };
        var scale = new ScaleTransform(1, 1);
        var scaled = new LayoutTransformControl { Child = canvas, LayoutTransform = scale };
        zoom.SelectionChanged += (_, _) => { scale.ScaleX = scale.ScaleY = Math.Pow(2, zoom.SelectedIndex); };
        var panel = new DockPanel();
        var info = new StackPanel { Children = { zoom, new TextBlock { Text = $"Original texture · fixed borders / stretching center · repeat X: {Flag("repeatX", false)}, Y: {Flag("repeatY", false)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) } } };
        DockPanel.SetDock(info, Dock.Top);
        panel.Children.Add(info);
        panel.Children.Add(new ScrollViewer { Content = scaled, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Content = new TabControl { Items = { new TabItem { Header = "Patch regions", Content = panel }, new TabItem { Header = "Definition", Content = new JsonDataView(() => asset.Data) } } };
    }
    public void Dispose()
    {
        _bitmap.Dispose();
    }
}
