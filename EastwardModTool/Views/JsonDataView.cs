using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardLib.Assets;
using EastwardModTool.Editing;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class JsonDataView : UserControl, IDisposable
{
    public sealed record Node(string Label, string[] Path, IReadOnlyList<Node> Children, string? ReferenceId = null)
    {
        public bool IsExpanded => Path.Length <= 1 || Path.Length == 3 && Path[0] == "map" && Path[2] == "body";
    }
    private readonly TextDocument? _document;
    private readonly Func<JsonNode?> _read;
    private readonly Action<JsonNode?>? _write;
    private readonly TreeView _tree = new() { Name = "DataTree" };
    private readonly TextBox _value = new() { Name = "PropertyValue", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Name = "ApplyProperty", Content = "Apply value", Margin = new Thickness(0, 8) };
    private string[]? _path;
    private string? _objectId;
    public string? ObjectId => _objectId;
    private readonly Stack<string?> _history = new();
    private readonly Button _reference = new() { Name = "OpenObjectReference", Content = "Open referenced object", IsEnabled = false, Margin = new Thickness(4) };
    private readonly Button _back = new() { Name = "ObjectBack", Content = "Back", IsEnabled = false, Margin = new Thickness(4) };
    private readonly Button _home = new() { Name = "ObjectRoot", Content = "Root object", Margin = new Thickness(4) };
    private readonly TextBlock _location = new() { Name = "ObjectLocation", Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _navigation = new();
    public JsonDataView(TextDocument document) : this(() => JsonNode.Parse(document.Text), document) { }
    public JsonDataView(Func<JsonNode?> read, TextDocument? document = null, Action<JsonNode?>? write = null)
    {
        _read = read;
        _document = document;
        _write = write ?? (document?.CanEdit == true ? data => document.Text = new JsonAsset(data).ToJson() : null);
        _tree.ItemTemplate = new FuncTreeDataTemplate<Node>((node, _) =>
        {
            var label = new TextBlock { Text = node?.Label, TextTrimming = TextTrimming.CharacterEllipsis };
            if (node?.ReferenceId != null)
            {
                ToolTip.SetTip(label, "Object reference: " + node.ReferenceId);
            }

            return label;
        }, node => node.Children);
        _tree.ItemContainerTheme = new Avalonia.Styling.ControlTheme(typeof(TreeViewItem))
        {
            BasedOn = (Avalonia.Styling.ControlTheme)Avalonia.Application.Current!.FindResource(typeof(TreeViewItem))!,
            Setters = { new Avalonia.Styling.Setter(TreeViewItem.IsExpandedProperty, new Avalonia.Data.Binding(nameof(Node.IsExpanded))) }
        };
        var inspector = new StackPanel { Margin = new Thickness(8), Children = { new TextBlock { Text = "Selected value", FontWeight = FontWeight.Bold }, _value, _apply, _status } };
        DockPanel.SetDock(inspector, Dock.Bottom);
        var panel = new DockPanel();
        _navigation.Children.Add(new WrapPanel { Children = { _back, _home, _reference } });
        _navigation.Children.Add(_location);
        DockPanel.SetDock(_navigation, Dock.Top);
        panel.Children.Add(_navigation);
        panel.Children.Add(inspector);
        panel.Children.Add(_tree);
        Content = panel;
        _value.IsReadOnly = _write == null;
        _apply.IsVisible = _write != null;
        _tree.SelectionChanged += (_, _) =>
        {
            if (_tree.SelectedItem is Node node)
            {
                _path = node.Path;
                _reference.IsEnabled = node.ReferenceId != null;
                _value.Text = new JsonAsset(At(_read(), _path)).ToJson();
                _apply.IsEnabled = _path.Length > 0;
            }
        };
        _tree.DoubleTapped += (_, _) => { if (_tree.SelectedItem is Node { ReferenceId: not null } node) { ShowObject(node.ReferenceId); } };
        _reference.Click += (_, _) => { if (_tree.SelectedItem is Node { ReferenceId: not null } node) { ShowObject(node.ReferenceId); } };
        _back.Click += (_, _) => { if (_history.TryPop(out var id)) { _objectId = id; _path = null; Refresh(); } };
        _home.Click += (_, _) => { if (_objectId != null) { _history.Push(_objectId); _objectId = null; _path = null; Refresh(); } };
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
            root = root is JsonArray array && int.TryParse(key, out var index) && index >= 0 && index < array.Count ? array[index] : root is JsonObject obj ? obj[key] : null;
        }

        return root;
    }
    public void ShowObject(string id)
    {
        var graph = new SerializedObjectGraph(_read());
        if (graph.Object(id) == null)
        {
            _status.Text = "Referenced object is missing: " + id;
            return;
        }
        if (_objectId == id)
        {
            return;
        }

        _history.Push(_objectId);
        _objectId = id;
        _path = null;
        Refresh();
    }
    private static Node Build(string name, JsonNode? value, string[] path, SerializedObjectGraph graph, int depth = 0)
    {
        var reference = graph.Reference(value);
        if (reference != null)
        {
            return new Node(name + " → " + graph.Label(reference), path, [], reference);
        }

        if (path.Length == 2 && path[0] == "map" && graph.Object(path[1]) != null)
        {
            name = graph.Label(path[1]) + " [" + path[1] + "]";
        }

        IReadOnlyList<Node> children = depth > 32 ? [] : value switch
        {
            JsonObject obj => obj.Select(pair => Build(pair.Key, pair.Value, [.. path, pair.Key], graph, depth + 1)).ToArray(),
            JsonArray array => array.Select((item, index) => Build(index.ToString(), item, [.. path, index.ToString()], graph, depth + 1)).ToArray(),
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
        if (_write == null || _path is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var data = _read()?.DeepClone();
            var parent = At(data, _path[..^1]) ?? throw new InvalidOperationException("The selected object was removed.");
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

            _write(data);
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
    public void Refresh()
    {
        var selectedPath = _path;
        _path = null;
        _value.Text = "";
        _apply.IsEnabled = false;
        _reference.IsEnabled = false;
        try
        {
            var data = _read();
            var graph = new SerializedObjectGraph(data);
            var roots = new List<Node>();
            var id = _objectId ?? graph.RootId;
            _navigation.IsVisible = graph.Objects != null;
            _back.IsEnabled = _history.Count > 0;
            _home.IsEnabled = _objectId != null;
            if (id != null && graph.Object(id) != null)
            {
                _location.Text = graph.Label(id) + " · " + id;
                roots.Add(graph.Body(id) != null ? Build(graph.Label(id), graph.Body(id), ["map", id, "body"], graph) : Build(graph.Label(id), graph.Object(id), ["map", id], graph));
            }
            else if (_objectId != null)
            {
                _location.Text = "Object removed: " + _objectId;
            }
            if (_objectId == null)
            {
                roots.Add(Build("All data", data, [], graph));
            }

            _tree.ItemsSource = roots;
            Node? Find(IEnumerable<Node> nodes)
            {
                foreach (var node in nodes)
                {
                    if (selectedPath != null && node.Path.SequenceEqual(selectedPath))
                    {
                        return node;
                    }

                    if (Find(node.Children) is Node child)
                    {
                        return child;
                    }
                }
                return null;
            }
            _tree.SelectedItem = Find(roots);
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
