using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EastwardLib.Assets;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class BitmapFontView : UserControl, IDisposable
{
    private readonly BitmapFontAsset _font;
    private readonly TextBox _sample = new() { Name = "BitmapFontSample", Text = "Eastward 0123456789 ABC abc", Margin = new Thickness(8) };
    private readonly Image _image = new() { Stretch = Stretch.None, Margin = new Thickness(8) };
    private Bitmap? _bitmap;
    public BitmapFontView(BitmapFontAsset font)
    {
        _font = font;
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
        var preview = new StackPanel { Children = { new TextBlock { Text = $"{font.GlyphCount:N0} glyphs · original bitmap pages and metrics", Margin = new Thickness(8) }, _sample, new ScrollViewer { Content = _image, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } };
        Content = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Sample", Content = preview },
                new TabItem { Header = "Definition", Content = AssetSyntax.ReadOnlySource("font.fnt", "font_bmfont", font.Text) }
            }
        };
        _sample.TextChanged += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        var image = _font.RenderText(_sample.Text ?? "");
        _bitmap?.Dispose();
        using var pixels = new MemoryStream(image.ToPng());
        _bitmap = new Bitmap(pixels);
        _image.Source = _bitmap;
    }
    public void Dispose()
    {
        _bitmap?.Dispose();
    }
}
