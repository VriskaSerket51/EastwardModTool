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
using EastwardModTool.Editing;
using AvaloniaEdit.Search;
using Avalonia.VisualTree;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class AssetEditorView : UserControl, IDisposable
{
    private sealed record EditorPosition(int Caret, int SelectionStart, int SelectionLength, double Horizontal, double Vertical, string? Entry, int Frame, string? Channel, int SourceTab);
    private readonly Dictionary<string, EditorPosition> _positions = new(StringComparer.Ordinal);
    private AssetDocument? _document;
    private DeckPackEditorView? _pack;
    private PackageBrowserView? _package;
    private Control? _presentation;
    private TextPresentationView? _sourceView;
    private Bitmap? _bitmap;
    private TextEditor? _text;
    private SearchPanel? _find;
    private Image? _image;
    private ComboBox? _entries;
    private ComboBox? _channels;
    private Slider? _frames;
    private ComboBox? _zoom;
    private ScrollViewer? _viewport;
    private TextBlock? _description;
    private Button? _play;
    private CheckBox? _loop;
    private readonly Avalonia.Threading.DispatcherTimer _playback = new();
    private bool _advancing;
    public bool IsPlaying => _playback.IsEnabled;

    public AssetEditorView()
    {
        _playback.Tick += (_, _) => AdvanceFrame();
        Unloaded += (_, _) => PausePlayback();
    }

    public void ShowDocument(AssetDocument document)
    {
        Dispose();
        _document = document;
        _document.PropertyChanged += DocumentChanged;
        if (document is UnavailableDocument unavailable)
        {
            Content = new TextBlock { Text = $"Preview unavailable · {document.AssetType}\n\n{unavailable.Reason}\n\nThe original asset metadata is shown in the inspector.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
            return;
        }
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
            _find = SearchPanel.Install(_text);
            _text.SyntaxHighlighting = AssetSyntax.ForSource(text.Name, text.AssetType, text.Text);
            _text.AddHandler(InputElement.KeyDownEvent, (_, args) =>
            {
                if (args.Source is TextBox || args.Source is Control source && source.FindAncestorOfType<TextBox>() != null)
                {
                    return;
                }

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
            _sourceView = new TextPresentationView(text, _text);
            Content = _sourceView.HasPreview ? _sourceView : _text;
            if (_positions.TryGetValue(document.Name, out var position))
            {
                _sourceView.SelectedIndex = Math.Min(position.SourceTab, _sourceView.Items.Count - 1);
                _text.Select(Math.Min(position.SelectionStart, _text.Text.Length), Math.Min(position.SelectionLength, Math.Max(0, _text.Text.Length - position.SelectionStart)));
                _text.CaretOffset = Math.Min(position.Caret, _text.Text.Length);
                var editor = _text;
                void RestorePosition(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
                {
                    editor.Loaded -= RestorePosition;
                    if (ReferenceEquals(_text, editor))
                    {
                        var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                        if (scroll != null)
                        {
                            scroll.Offset = new Vector(position.Horizontal, position.Vertical);
                        }
                    }
                }
                editor.Loaded += RestorePosition;
            }
            return;
        }
        var resource = (ResourceDocument)document;
        if (resource.Current is DeckPackAsset)
        {
            _positions.TryGetValue(document.Name, out var savedPack);
            _pack = new DeckPackEditorView(resource, savedPack?.Entry);
            Content = _pack;
            return;
        }
        _presentation = AssetPresentationFactory.Create(resource, _positions.GetValueOrDefault(document.Name)?.Entry);
        _package = _presentation as PackageBrowserView;
        if (_presentation != null)
        {
            Content = _presentation;
            return;
        }
        _entries = new ComboBox { ItemsSource = resource.Entries, MinWidth = 240, SelectedIndex = resource.Entries.Count > 0 ? 0 : -1 };
        _frames = new Slider { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 160 };
        _description = new TextBlock { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
        _image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(24), UseLayoutRounding = true };
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
        var panel = new DockPanel();
        _entries.MaxWidth = 320;
        _entries.Margin = new Thickness(6);
        _frames.Margin = new Thickness(6);
        _play = new Button { Name = "PlayAnimation", Content = "Play", Margin = new Thickness(6), Padding = new Thickness(12, 7) };
        _loop = new CheckBox { Name = "LoopAnimation", Content = "Loop", IsChecked = true, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        _play.Click += (_, _) =>
        {
            if (IsPlaying)
            {
                PausePlayback();
            }
            else
            {
                StartPlayback();
            }
        };
        _channels = new ComboBox { Name = "DeckChannel", ItemsSource = resource.Current is DeckAsset deck ? deck.Textures.Keys.ToArray() : [], SelectedIndex = 0, Margin = new Thickness(6), IsVisible = resource.Current is DeckAsset };
        _channels.SelectionChanged += (_, _) => UpdateResource(false);
        _zoom = new ComboBox { Name = "ImageZoom", ItemsSource = new[] { "Fit", "1×", "2×", "4×", "8×" }, SelectedIndex = 0, Margin = new Thickness(6) };
        _zoom.SelectionChanged += (_, _) => ApplyZoom();
        var controls = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6), Children = { _entries, _channels, _play, _loop, _frames, _zoom } };
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        DockPanel.SetDock(_description, Dock.Bottom);
        panel.Children.Add(_description);
        _viewport = new ScrollViewer { Content = _image, Background = Brush.Parse("#252932"), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(_viewport);
        _entries.SelectionChanged += (_, _) => UpdateResource();
        _frames.ValueChanged += (_, _) =>
        {
            if (!_advancing)
            {
                PausePlayback();
            }

            UpdateResource(false);
        };
        Content = resource.Current is JsonAsset definition ? new TabControl
        {
            Name = "DeckViews",
            Items =
        {
            new TabItem { Header = "Preview", Content = panel },
            new TabItem { Header = "Definition", Content = AssetSyntax.ReadOnlySource(document.Name, "data_json", definition.ToJson()) }
        }
        } : panel;
        UpdateResource();
        if (_positions.TryGetValue(document.Name, out var saved))
        {
            if (saved.Entry != null && resource.Entries.Contains(saved.Entry))
            {
                _entries.SelectedItem = saved.Entry;
            }

            _frames.Value = Math.Min(saved.Frame, _frames.Maximum);
            if (saved.Channel != null && _channels.Items.Contains(saved.Channel))
            {
                _channels.SelectedItem = saved.Channel;
            }
        }
    }

    public bool Find(bool replace = false)
    {
        if (_find == null)
        {
            return false;
        }

        _sourceView?.ShowSource();
        _find.IsReplaceMode = replace;
        _find.Open();
        _find.Reactivate();
        return true;
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
            if (_presentation is SceneHierarchyView)
            {
                return;
            }

            if (resource.Current is DeckPackAsset || _presentation != null)
            {
                ShowDocument(resource);
                return;
            }
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
            PausePlayback();
            _frames.Maximum = entry == null ? 0 : Math.Max(0, resource.GetFrameCount(entry) - 1);
            _frames.Value = 0;
            _frames.IsVisible = resource.CanAnimate;
            _play!.IsVisible = _loop!.IsVisible = resource.CanAnimate;
            _play.IsEnabled = entry != null && resource.CanAnimate && resource.GetFrameCount(entry) > 0;
        }
        var frame = (int)(_frames?.Value ?? 0);
        _bitmap?.Dispose();
        _bitmap = null;
        _find?.Uninstall();
        _find = null;
        var image = resource.GetImage(entry, frame, _channels?.SelectedItem as string);
        if (image != null)
        {
            using var pixels = new MemoryStream(image.ToPng());
            _bitmap = new Bitmap(pixels);
            _image.Source = _bitmap;
            ApplyZoom();
            _description!.Text = $"{image.Width} × {image.Height} · RGBA" + (resource.CanAnimate ? $" · Frame {frame + 1}/{resource.GetFrameCount(entry!)}" : "");
        }
        else
        {
            _image.Source = null;
            _description!.Text = resource.Current is PackageAsset ? $"{resource.Entries.Count} source files. Export the package, edit its sources, then import the folder." : "Binary resource. Export the file or import a replacement.";
            if (resource.Current is LuaAsset lua && lua.IsBytecode)
            {
                _description.Text = "LuaJIT source preview unavailable. Export the original bytes or import Lua source. " + resource.PreviewWarning;
            }
        }
        if (resource.Current is DeckAsset deck)
        {
            _description!.Text = image == null ? "No renderable geometry or parent texture for this deck. See Definition." : $"{image.Width} × {image.Height} · {deck.Data?["type"]} · XY projection · depth and scene lighting are not applied";
        }
        _entries!.IsVisible = resource.Entries.Count > 0;
    }

    private void ApplyZoom()
    {
        if (_image == null || _viewport == null || _zoom == null)
        {
            return;
        }

        var fit = _zoom.SelectedIndex <= 0;
        _image.Stretch = fit ? Stretch.Uniform : Stretch.Fill;
        _image.Width = fit || _bitmap == null ? double.NaN : _bitmap.PixelSize.Width * Math.Pow(2, _zoom.SelectedIndex - 1) / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _image.Height = fit || _bitmap == null ? double.NaN : _bitmap.PixelSize.Height * Math.Pow(2, _zoom.SelectedIndex - 1) / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _viewport.HorizontalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _viewport.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void StartPlayback()
    {
        if (_document is not ResourceDocument resource || !resource.CanAnimate || _entries?.SelectedItem is not string entry || resource.GetFrameCount(entry) == 0)
        {
            return;
        }

        if (_loop?.IsChecked != true && _frames!.Value == _frames.Maximum)
        {
            _frames.Value = 0;
        }

        _playback.Interval = resource.GetFrameDuration(entry, (int)_frames!.Value);
        _play!.Content = "Pause";
        _playback.Start();
    }

    private void PausePlayback()
    {
        _playback.Stop();
        if (_play != null)
        {
            _play.Content = "Play";
        }
    }

    private void AdvanceFrame()
    {
        if (_document is not ResourceDocument resource || _entries?.SelectedItem is not string entry || _frames == null)
        {
            PausePlayback();
            return;
        }
        try
        {
            var next = (int)_frames.Value + 1;
            if (next >= resource.GetFrameCount(entry))
            {
                if (_loop?.IsChecked != true)
                {
                    PausePlayback();
                    return;
                }
                next = 0;
            }
            _advancing = true;
            _frames.Value = next;
            _playback.Interval = resource.GetFrameDuration(entry, next);
        }
        catch (Exception error)
        {
            PausePlayback();
            _description!.Text = error.Message;
            System.Diagnostics.Trace.WriteLine(error);
        }
        finally { _advancing = false; }
    }

    public void Clear()
    {
        Dispose();
        _positions.Clear();
    }

    public void Dispose()
    {
        PausePlayback();
        if (_document != null)
        {
            var offset = _text?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
            _positions[_document.Name] = new EditorPosition(_text?.CaretOffset ?? 0, _text?.SelectionStart ?? 0, _text?.SelectionLength ?? 0, offset.X, offset.Y, _pack?.SelectedName ?? _package?.SelectedName ?? _entries?.SelectedItem as string, (int)(_frames?.Value ?? 0), _channels?.SelectedItem as string, _sourceView?.SelectedIndex ?? 0);
        }
        _find?.Uninstall();
        _find = null;
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        _pack?.Dispose();
        _pack = null;
        if (_presentation is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _presentation = null;
        _package = null;
        _sourceView?.Dispose();
        _sourceView = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _document = null;
        _text = null;
        _image = null;
        _entries = null;
        _channels = null;
        _frames = null;
        _zoom = null;
        _viewport = null;
        _play = null;
        _loop = null;
        Content = null;
    }
}
