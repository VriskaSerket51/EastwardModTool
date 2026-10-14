using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

public sealed class BinaryAssetView : UserControl
{
    private readonly byte[] _bytes;
    private readonly TextBox _hex = new() { Name = "BinaryHex", IsReadOnly = true, AcceptsReturn = true, FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,DejaVu Sans Mono") };
    private readonly NumericUpDown _offset = new() { Minimum = 0, Increment = 4096, FormatString = "0", MinWidth = 180 };
    public BinaryAssetView(BinaryAsset asset, string type)
    {
        _bytes = asset.Encode();
        _offset.Maximum = Math.Max(0, _bytes.Length - 1);
        var signature = Encoding.ASCII.GetString(_bytes.AsSpan(0, Math.Min(4, _bytes.Length))).Replace("\0", "·");
        var note = type switch
        {
            "movie" => "Encoded video · export to play in a video player",
            "font_ttf" => "Font resource · export to inspect in a font application",
            "fs_project" => "FMOD bank resource · event playback requires FMOD",
            "lut_texture" => "Color grading data",
            _ => "Binary resource"
        };
        var header = new StackPanel { Margin = new Thickness(8), Spacing = 8, Children = { new TextBlock { Text = $"{note}\n{_bytes.Length:N0} bytes · signature {signature}", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, _offset } };
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(_hex);
        Content = panel;
        _offset.ValueChanged += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        var text = new StringBuilder();
        var start = (int)(_offset.Value ?? 0);
        var end = Math.Min(_bytes.Length, start + 4096);
        for (var offset = start; offset < end; offset += 16)
        {
            var row = _bytes.AsSpan(offset, Math.Min(16, end - offset));
            text.Append(offset.ToString("X8")).Append("  ").Append(Convert.ToHexString(row).Chunk(2).Select(chars => new string(chars)).Aggregate("", (current, item) => current + item + " ").PadRight(48)).Append("  ");
            foreach (var value in row)
            {
                text.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }

            text.AppendLine();
        }
        _hex.Text = text.ToString();
    }
}
