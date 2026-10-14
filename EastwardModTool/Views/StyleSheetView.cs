using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class StyleSheetView : UserControl, IDisposable
{
    public sealed record Style(string Id, string Label);
    private readonly TextDocument _document;
    private readonly ListBox _styles = new() { Name = "StyleList", MinHeight = 90 };
    private readonly TextBox _name = new() { Name = "StyleName" };
    private readonly TextBox _font = new() { Name = "StyleFont" };
    private readonly NumericUpDown _size = new() { Name = "StyleSize", Minimum = 1, Maximum = 16384, Increment = 1, FormatString = "0" };
    private readonly NumericUpDown[] _rgba = Enumerable.Range(0, 4).Select(index => new NumericUpDown { Name = "StyleColor" + index, Minimum = 0, Maximum = 1, Increment = 0.05m, FormatString = "0.###" }).ToArray();
    private readonly CheckBox _scale = new() { Name = "StyleAllowScale", Content = "Allow font scaling" };
    private readonly Button _apply = new() { Name = "ApplyStyle", Content = "Apply style", Margin = new Thickness(0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _identity = new() { Name = "StyleIdentity", TextWrapping = TextWrapping.Wrap };
    private readonly Border _swatch = new() { Name = "StyleSwatch", Height = 35, Margin = new Thickness(0, 8), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    private readonly JsonDataView _objects;
    private readonly StackPanel _fields;
    private readonly TabControl _tabs;
    private string? _selected;

    public StyleSheetView(TextDocument document)
    {
        _document = document;
        _objects = new JsonDataView(document);
        _styles.ItemTemplate = new FuncDataTemplate<Style>((style, _) => new TextBlock { Text = style?.Label, Margin = new Thickness(4), TextTrimming = TextTrimming.CharacterEllipsis });
        _styles.SelectionChanged += (_, _) => Select();
        _fields = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        _fields.Children.Add(_identity);
        Field("Name", _name);
        Field("Font asset path (leave blank for the fallback font)", _font);
        Field("Size", _size);
        var colors = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var index = 0; index < 4; index++)
        {
            colors.Children.Add(new StackPanel { Width = 145, Margin = new Thickness(0, 0, 8, 0), Children = { new TextBlock { Text = new[] { "Red", "Green", "Blue", "Alpha" }[index] + " · 0–1" }, _rgba[index] } });
        }
        _fields.Children.Add(colors);
        foreach (var channel in _rgba) channel.ValueChanged += (_, _) => UpdateSwatch();
        _fields.Children.Add(_swatch);
        _fields.Children.Add(_scale);
        _fields.Children.Add(new TextBlock { Text = "Allow scaling uses the original font size and scales it to the requested size. Otherwise the engine changes the font size. The swatch previews the RGBA colour.", TextWrapping = TextWrapping.Wrap });
        _fields.Children.Add(_apply);
        _fields.Children.Add(_status);
        _apply.IsVisible = document.CanEdit;
        _apply.Click += (_, _) => Apply();
        var panel = new DockPanel();
        var list = new DockPanel { MaxHeight = 180 };
        var heading = new TextBlock { Text = "Text styles · original order", FontWeight = FontWeight.Bold, Margin = new Thickness(10) };
        DockPanel.SetDock(heading, Dock.Top);
        list.Children.Add(heading);
        list.Children.Add(_styles);
        DockPanel.SetDock(list, Dock.Top);
        panel.Children.Add(list);
        panel.Children.Add(new ScrollViewer { Content = _fields });
        _tabs = new TabControl { Items = { new TabItem { Header = "Style properties", Content = panel }, new TabItem { Header = "Object data", Content = _objects } } };
        Content = _tabs;
        document.PropertyChanged += Changed;
        Loaded += (_, _) => Refresh();
        Refresh();
    }
    private void Field(string label, Control control)
    {
        _fields.Children.Add(new TextBlock { Text = label });
        _fields.Children.Add(control);
    }
    private void Refresh()
    {
        var selected = _selected;
        try
        {
            var graph = new SerializedObjectGraph(JsonNode.Parse(_document.Text));
            var styles = graph.RootId == null ? [] : graph.References(graph.RootId, "styles").Select(id => new Style(id, graph.Label(id))).ToArray();
            _styles.ItemsSource = styles;
            _styles.SelectedItem = styles.FirstOrDefault(style => style.Id == selected) ?? styles.FirstOrDefault();
            _status.Text = styles.Length == 0 ? "No referenced text styles. Inspect Object data or Source for missing references." : "Changes preserve object IDs, references and additional fields.";
        }
        catch (System.Text.Json.JsonException error)
        {
            _styles.ItemsSource = null;
            _fields.IsEnabled = false;
            _status.Text = "Source contains invalid JSON: " + error.Message;
        }
    }
    private void Select()
    {
        _selected = (_styles.SelectedItem as Style)?.Id;
        if (_selected == null)
        {
            _fields.IsEnabled = false;
            return;
        }
        var graph = new SerializedObjectGraph(JsonNode.Parse(_document.Text));
        var body = graph.Body(_selected) as JsonObject;
        _fields.IsEnabled = body != null;
        if (body == null)
        {
            return;
        }

        _identity.Text = graph.Label(_selected!) + " · " + _selected;
        _name.Text = body["name"]?.ToString() ?? "";
        _font.Text = body["font"] is JsonValue value && value.TryGetValue<string>(out var font) ? font : "";
        _size.Value = Number(body["size"], 20);
        var color = body["color"] as JsonArray;
        for (var index = 0; index < 4; index++)
        {
            _rgba[index].Value = Number(color != null && color.Count > index ? color[index] : null, 1);
        }

        _scale.IsChecked = body["allowScale"] is not JsonValue scale || !scale.TryGetValue<bool>(out var allow) || allow;
        UpdateSwatch();
        if (_tabs.SelectedIndex == 0)
        {
            _objects.ShowObject(_selected!);
        }

        _name.IsReadOnly = _font.IsReadOnly = !_document.CanEdit;
        _size.IsEnabled = _scale.IsEnabled = _document.CanEdit;
        foreach (var channel in _rgba)
        {
            channel.IsEnabled = _document.CanEdit;
        }
    }
    private void UpdateSwatch()
    {
        _swatch.Background = new SolidColorBrush(Color.FromArgb(Channel(3), Channel(0), Channel(1), Channel(2)));
    }
    private byte Channel(int index) => (byte)Math.Round(Math.Clamp(_rgba[index].Value ?? 1, 0, 1) * 255);
    private static decimal Number(JsonNode? node, decimal fallback) => decimal.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    private void Apply()
    {
        if (!_document.CanEdit || _selected == null)
        {
            return;
        }

        try
        {
            if (_size.Value is not decimal size || size < 1 || size != decimal.Truncate(size))
            {
                _status.Text = "Size must be a positive integer.";
                return;
            }
            var data = JsonNode.Parse(_document.Text);
            var body = new SerializedObjectGraph(data).Body(_selected) as JsonObject ?? throw new InvalidOperationException("The selected style was removed.");
            var original = body.DeepClone();
            body["name"] = _name.Text ?? "";
            body["font"] = string.IsNullOrEmpty(_font.Text) ? JsonValue.Create(false) : JsonValue.Create(_font.Text);
            body["size"] = size;
            body["color"] = new JsonArray(_rgba.Select(channel => (JsonNode?)JsonValue.Create(channel.Value ?? 1)).ToArray());
            body["allowScale"] = _scale.IsChecked == true;
            if (JsonNode.DeepEquals(original, body))
            {
                return;
            }

            _document.Text = new JsonAsset(data).ToJson();
            _status.Text = "Style updated. Undo and Source share the same document.";
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { _status.Text = error.Message; }
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
        _objects.Dispose();
    }
}
