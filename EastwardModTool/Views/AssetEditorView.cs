using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class AssetEditorView : UserControl, IDisposable
{
    private AssetDocument? _document;
    private Bitmap? _bitmap;
    private TextEditor? _text;
    private Image? _image;
    private ComboBox? _entries;
    private Slider? _frames;
    private TextBlock? _description;

    public void ShowDocument(AssetDocument document)
    {
        Dispose();
        _document = document;
        _document.PropertyChanged += DocumentChanged;
        if (document is TextDocument text)
        {
            _text = new TextEditor
            {
                Text = text.Text,
                ShowLineNumbers = true,
                IsReadOnly = !text.CanEdit,
                FontFamily = new FontFamily("Cascadia Code,Consolas,DejaVu Sans Mono"),
                FontSize = 13,
                WordWrap = false,
                Padding = new Thickness(16)
            };
            _text.Document.UndoStack.SizeLimit = 0;
            var language = text.Name.StartsWith('@') || text.Text.TrimStart().StartsWith('{') || text.Text.TrimStart().StartsWith('[') ? "Json" : Path.GetExtension(text.Name) is ".ml" or ".graphml" ? "XML" : null;
            _text.SyntaxHighlighting = language == null ? null : HighlightingManager.Instance.GetDefinition(language);
            _text.AddHandler(InputElement.KeyDownEvent, (_, args) =>
            {
                if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || args.Key is not (Key.Z or Key.Y))
                {
                    return;
                }

                if (args.Key == Key.Y || args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    text.Redo();
                }
                else
                {
                    text.Undo();
                }

                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            _text.TextChanged += (_, _) =>
            {
                if (ReferenceEquals(_document, text) && text.CanEdit && _text != null && _text.Text != text.Text)
                {
                    text.Text = _text.Text ?? "";
                }
            };
            Content = _text;
            return;
        }
        var resource = (ResourceDocument)document;
        _entries = new ComboBox { ItemsSource = resource.Entries, MinWidth = 240, SelectedIndex = resource.Entries.Count > 0 ? 0 : -1 };
        _frames = new Slider { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 160 };
        _description = new TextBlock { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
        _image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
        var panel = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12), Children = { _entries, _frames } };
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        DockPanel.SetDock(_description, Dock.Bottom);
        panel.Children.Add(_description);
        panel.Children.Add(new Border { Background = Brush.Parse("#252932"), Child = _image });
        _entries.SelectionChanged += (_, _) => UpdateResource();
        _frames.ValueChanged += (_, _) => UpdateResource(false);
        Content = panel;
        UpdateResource();
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_document is TextDocument text && args.PropertyName == nameof(TextDocument.Text) && _text != null && _text.Text != text.Text)
        {
            var caret = _text.CaretOffset;
            _text.Text = text.Text;
            _text.CaretOffset = Math.Min(caret, text.Text.Length);
        }
        if (_document is ResourceDocument resource && args.PropertyName == nameof(ResourceDocument.Current))
        {
            _entries!.ItemsSource = resource.Entries;
            _entries.SelectedIndex = resource.Entries.Count > 0 ? 0 : -1;
            UpdateResource();
        }
    }

    private void UpdateResource(bool resetFrame = true)
    {
        if (_document is not ResourceDocument resource || _image == null)
        {
            return;
        }

        var entry = _entries?.SelectedItem as string;
        if (resetFrame && _frames != null)
        {
            _frames.Maximum = entry == null ? 0 : Math.Max(0, resource.GetFrameCount(entry) - 1);
            _frames.Value = 0;
            _frames.IsVisible = resource.Current is MSpriteAsset;
        }
        var frame = (int)(_frames?.Value ?? 0);
        _bitmap?.Dispose();
        _bitmap = null;
        var image = resource.GetImage(entry, frame);
        if (image != null)
        {
            using var pixels = new MemoryStream(image.ToPng());
            _bitmap = new Bitmap(pixels);
            _image.Source = _bitmap;
            _description!.Text = $"{image.Width} × {image.Height} · RGBA" + (resource.Current is MSpriteAsset ? $" · Frame {frame + 1}/{resource.GetFrameCount(entry!)}" : "");
        }
        else
        {
            _image.Source = null;
            _description!.Text = resource.Current is PackageAsset ? $"{resource.Entries.Count} source files. Export the package, edit its sources, then import the folder." : "Binary resource. Export the file or import a replacement.";
            if (resource.Current is LuaAsset lua && lua.IsBytecode)
            {
                _description.Text = "LuaJIT bytecode. Open the game with a native decompiler to edit Lua source, or import a Lua file.";
            }
        }
        _entries!.IsVisible = resource.Entries.Count > 0;
    }

    public void Dispose()
    {
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        _bitmap?.Dispose();
        _bitmap = null;
        _document = null;
        _text = null;
        _image = null;
        Content = null;
    }
}
