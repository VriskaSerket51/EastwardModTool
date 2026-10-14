using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class CodeTilesetView : UserControl, IDisposable
{
    private readonly TextDocument _document;
    public CodeTilesetView(TextDocument document)
    {
        _document = document;
        document.PropertyChanged += Changed;
        Refresh();
    }
    private void Refresh()
    {
        try
        {
            var data = JsonNode.Parse(_document.Text)!;
            var tiles = data["tiles"]!.AsObject();
            var swatches = new WrapPanel { Name = "CodeTiles", Orientation = Orientation.Horizontal };
            foreach (var (name, tile) in tiles)
            {
                var color = tile?["color"]?.ToString() ?? data["default"]?["color"]?.ToString() ?? "#606060";
                swatches.Children.Add(new Border { Width = 110, Height = 85, Margin = new Thickness(6), Padding = new Thickness(6), Background = Brush.Parse(color), Child = new TextBlock { Text = name + "\nID " + tile?["_id"] + "\n" + (tile?["shape"]?.ToString() ?? data["default"]?["shape"]?.ToString()), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White } });
            }
            Content = new ScrollViewer { Content = swatches };
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException) { Content = new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap }; }
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TextDocument.Text))
        {
            Refresh();
        }
    }
    public void Dispose()
    {
        _document.PropertyChanged -= Changed;
    }
}
