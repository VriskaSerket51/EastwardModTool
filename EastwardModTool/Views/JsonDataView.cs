using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class JsonDataView : UserControl, IDisposable
{
    public sealed record Node(string Label, string[] Path, IReadOnlyList<Node> Children)
    {
        public bool IsExpanded => Path.Length <= 1 || Path.Length == 3 && Path[0] == "map" && Path[2] == "body";
    }
    private readonly TextDocument? _document;
    private readonly Func<JsonNode?> _read;
    private readonly TreeView _tree = new() { Name = "DataTree" };
    private readonly TextBox _value = new() { Name = "PropertyValue", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Name = "ApplyProperty", Content = "Apply value", Margin = new Thickness(0, 8) };
    private string[]? _path;
    public JsonDataView(TextDocument document) : this(() => JsonNode.Parse(document.Text), document) { }
    public JsonDataView(Func<JsonNode?> read, TextDocument? document = null)
    {
        _read = read;
        _document = document;
        _tree.ItemTemplate = new FuncTreeDataTemplate<Node>((node, _) => new TextBlock { Text = node?.Label, TextTrimming = TextTrimming.CharacterEllipsis }, node => node.Children);
        _tree.ItemContainerTheme = new Avalonia.Styling.ControlTheme(typeof(TreeViewItem))
        {
            BasedOn = (Avalonia.Styling.ControlTheme)Avalonia.Application.Current!.FindResource(typeof(TreeViewItem))!,
            Setters = { new Avalonia.Styling.Setter(TreeViewItem.IsExpandedProperty, new Avalonia.Data.Binding(nameof(Node.IsExpanded))) }
        };
        var inspector = new StackPanel { Margin = new Thickness(8), Children = { new TextBlock { Text = "Selected value", FontWeight = FontWeight.Bold }, _value, _apply, _status } };
        DockPanel.SetDock(inspector, Dock.Bottom);
        var panel = new DockPanel();
        panel.Children.Add(inspector);
        panel.Children.Add(_tree);
        Content = panel;
        _value.IsReadOnly = document?.CanEdit != true;
        _apply.IsVisible = document?.CanEdit == true;
        _tree.SelectionChanged += (_, _) =>
        {
            if (_tree.SelectedItem is Node node)
            {
                _path = node.Path;
                _value.Text = new JsonAsset(At(_read(), _path)).ToJson();
                _apply.IsEnabled = _path.Length > 0;
            }
        };
        _apply.Click += (_, _) => Apply();
        if (document != null)
        {
            document.PropertyChanged += Changed;
        }

        Loaded += (_, _) => Refresh();
        Refresh();
    }
    private static JsonNode? At(JsonNode? root, IEnumerable<string> path)
    {
        foreach (var key in path)
        {
            root = root is JsonArray array ? array[int.Parse(key)] : root?[key];
        }

        return root;
    }
    private static Node Build(string name, JsonNode? value, string[] path, int depth = 0)
    {
        IReadOnlyList<Node> children = depth > 32 ? [] : value switch
        {
            JsonObject obj => obj.Select(pair => Build(pair.Key, pair.Value, [.. path, pair.Key], depth + 1)).ToArray(),
            JsonArray array => array.Select((item, index) => Build(index.ToString(), item, [.. path, index.ToString()], depth + 1)).ToArray(),
            _ => []
        };
        var display = value is JsonObject ? $"{{{children.Count}}}" : value is JsonArray ? $"[{children.Count}]" : value?.ToString().Replace("\n", " ") ?? "null";
        if (display.Length > 100)
        {
            display = display[..100] + "…";
        }

        return new Node(name + " : " + display, path, children);
    }
    private void Apply()
    {
        if (_document?.CanEdit != true || _path is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var data = _read();
            var parent = At(data, _path[..^1]);
            var value = JsonNode.Parse(_value.Text ?? "null");
            if (JsonNode.DeepEquals(At(data, _path), value))
            {
                _status.Text = "Value is unchanged.";
                return;
            }
            if (parent is JsonArray array)
            {
                array[int.Parse(_path[^1])] = value;
            }
            else
            {
                parent![_path[^1]] = value;
            }

            _document.Text = new JsonAsset(data).ToJson();
            _status.Text = "Value updated. Build validation checks the complete asset.";
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
    private void Refresh()
    {
        _path = null;
        _value.Text = "";
        _apply.IsEnabled = false;
        try
        {
            var data = _read();
            var roots = new List<Node>();
            var id = data is JsonObject root && root["root"] is JsonValue rootId ? rootId.ToString() : null;
            if (id != null && data?["map"] is JsonObject map && map[id]?["body"] is JsonObject body)
            {
                roots.Add(Build(map[id]?["model"]?.ToString() ?? "Properties", body, ["map", id, "body"]));
            }

            roots.Add(Build("All data", data, []));
            _tree.ItemsSource = roots;
            _status.Text = "JSON values retain their original types. Strings use quotes.";
        }
        catch (System.Text.Json.JsonException error) { _tree.ItemsSource = null; _status.Text = "Source contains invalid JSON: " + error.Message; }
    }
    public void Dispose()
    {
        if (_document != null)
        {
            _document.PropertyChanged -= Changed;
        }
    }
}
