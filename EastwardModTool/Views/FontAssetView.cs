using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

public sealed class FontAssetView : UserControl, IDisposable
{
    private sealed class Sample : Control
    {
        public GlyphTypeface? Face;
        public string Text = "Eastward 0123456789 ABC abc 한글 日本語";
        public override void Render(DrawingContext context)
        {
            if (Face == null)
            {
                return;
            }

            var glyphs = Text.EnumerateRunes().Select(rune => Face.CharacterToGlyphMap.TryGetGlyph(rune.Value, out var glyph) ? glyph : (ushort)0).ToArray();
            using var run = new GlyphRun(Face, 32, Text.AsMemory(), glyphs, new Point(8, 50), 0);
            context.DrawGlyphRun(Brushes.White, run);
        }
    }
    private sealed class ResourceFonts : Avalonia.Media.Fonts.FontCollectionBase
    {
        public override Uri Key { get; } = new Uri("fonts:Eastward-" + Guid.NewGuid().ToString("N"));
        public GlyphTypeface Read(Stream stream)
        {
            if (!TryAddGlyphTypeface(stream, out var face))
            {
                throw new InvalidDataException("Font resource could not be decoded");
            }

            return face;
        }
    }
    private readonly ResourceFonts _fonts = new();
    private readonly MemoryStream _input;
    private readonly GlyphTypeface _face;
    public FontAssetView(BinaryAsset asset)
    {
        _input = new MemoryStream(asset.Encode());
        FontManager.Current.AddFontCollection(_fonts);
        try
        {
            _face = _fonts.Read(_input);
        }
        catch { FontManager.Current.RemoveFontCollection(_fonts.Key); _input.Dispose(); throw; }
        var sample = new Sample { Face = _face, MinHeight = 120, MinWidth = 700, Name = "FontSample" };
        var text = new TextBox { Text = sample.Text, Name = "FontSampleText", Margin = new Thickness(8) };
        text.TextChanged += (_, _) => { sample.Text = text.Text ?? ""; sample.InvalidateVisual(); };
        Content = new StackPanel { Children = { new TextBlock { Text = $"{_face.FamilyName} · {_face.GlyphCount:N0} glyphs", Margin = new Thickness(8) }, text, new ScrollViewer { Content = sample, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } };
    }
    public void Dispose()
    {
        FontManager.Current.RemoveFontCollection(_fonts.Key);
        _input.Dispose();
    }
}
