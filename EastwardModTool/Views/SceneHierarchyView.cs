using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using AvaloniaEdit;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class SceneHierarchyView : UserControl, IDisposable
{
    public sealed record Entity(string Label, string? Id, IReadOnlyList<Entity> Children);
    private readonly ComboBox _groups = new() { Name = "SceneGroups", Margin = new Thickness(8), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TreeView _tree = new() { Name = "SceneHierarchy" };
    private readonly ContentControl _details = new();
    private readonly TextBlock _status = new() { Name = "SceneStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextEditor _source = new() { Name = "SceneGroupSource", ShowLineNumbers = true, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace") };
    private readonly Grid _layout = new();
    private readonly AssetDocument? _document;
    private readonly Func<IReadOnlyDictionary<string, JsonNode?>> _read;
    private readonly Action<string, JsonNode?>? _write;
    private JsonDataView? _inspector;
    private string? _selectedId;
    private string? _groupName;
    private bool _refreshing;

    public SceneHierarchyView(IReadOnlyDictionary<string, JsonNode?> data) : this(() => data, null, null) { }
    public SceneHierarchyView(TextDocument document) : this(() => new Dictionary<string, JsonNode?> { ["Entities"] = JsonNode.Parse(document.Text) }, document,
        document.CanEdit ? (_, data) => document.Text = new JsonAsset(data).ToJson() : null)
    {
    }
    public SceneHierarchyView(ResourceDocument document) : this(() => document.SceneData, document, document.CanEdit ? document.ReplaceSceneData : null) { }
    private SceneHierarchyView(Func<IReadOnlyDictionary<string, JsonNode?>> read, AssetDocument? document, Action<string, JsonNode?>? write)
    {
        _read = read;
        _document = document;
        _write = write;
        _tree.ItemTemplate = new FuncTreeDataTemplate<Entity>((node, _) =>
        {
            var text = new TextBlock { Text = node?.Label, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(text, node?.Id);
            return text;
        }, node => node.Children);
        _tree.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _tree.SelectedItem is not Entity entity)
            {
                return;
            }

            _selectedId = entity.Id;
            if (entity.Id != null)
            {
                _inspector?.ShowObject(entity.Id);
            }
        };
        _groups.SelectionChanged += (_, _) => { if (!_refreshing) { _selectedId = null; Refresh(); } };
        _source.IsReadOnly = write == null;
        var apply = new Button { Name = "ApplySceneSource", Content = "Apply group source", Margin = new Thickness(8), IsVisible = write != null };
        apply.Click += (_, _) =>
        {
            try
            {
                if (_groups.SelectedItem is string name)
                {
                    _write?.Invoke(name, JsonNode.Parse(_source.Text));
                }
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { _status.Text = error.Message; }
        };
        var sourcePanel = new DockPanel();
        DockPanel.SetDock(apply, Dock.Bottom);
        sourcePanel.Children.Add(apply);
        sourcePanel.Children.Add(_source);
        Control tabs = document is TextDocument ? _details : new TabControl { Items = { new TabItem { Header = "Object properties", Content = _details }, new TabItem { Header = "Group source", Content = sourcePanel } } };
        _layout.Children.Add(_tree);
        _layout.Children.Add(tabs);
        void Layout()
        {
            var wide = Bounds.Width >= 620;
            _layout.ColumnDefinitions = new ColumnDefinitions(wide ? "2*,3*" : "*");
            _layout.RowDefinitions = new RowDefinitions(wide ? "*" : "160,*");
            Grid.SetColumn(tabs, wide ? 1 : 0);
            Grid.SetRow(tabs, wide ? 0 : 1);
        }
        SizeChanged += (_, _) => Layout();
        Layout();
        var panel = new DockPanel();
        DockPanel.SetDock(_groups, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_groups);
        panel.Children.Add(_status);
        panel.Children.Add(_layout);
        Content = panel;
        if (document != null)
        {
            document.PropertyChanged += Changed;
        }

        Loaded += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var groups = _read();
            var name = _groups.SelectedItem as string;
            if (name == null || !groups.ContainsKey(name))
            {
                name = groups.Keys.FirstOrDefault(key => key.Contains("default", StringComparison.OrdinalIgnoreCase)) ?? groups.Keys.FirstOrDefault(key => key.EndsWith(".scene_group", StringComparison.Ordinal)) ?? groups.Keys.FirstOrDefault();
            }

            _groups.ItemsSource = groups.Keys.ToArray();
            _groups.SelectedItem = name;
            if (name != _groupName)
            {
                _inspector?.Dispose();
                _inspector = null;
                _details.Content = null;
            }
            _groupName = name;
            if (name == null)
            {
                _tree.ItemsSource = null;
                return;
            }
            var data = groups[name];
            var graph = new SerializedObjectGraph(data);
            var roots = data is JsonObject obj ? Values(obj["entities"]).Select(entry => Build(entry, graph, [], 0)).ToArray() : [];
            if (roots.Length == 0)
            {
                roots = graph.RootId != null ? [new Entity(graph.Label(graph.RootId), graph.RootId, [])] : [new Entity("Scene configuration", null, [])];
            }
            _tree.ItemsSource = roots;
            _inspector ??= new JsonDataView(() => _read().GetValueOrDefault(name), write: _write == null ? null : value => _write(name, value));
            _details.Content = _inspector;
            var selected = Flatten(roots).FirstOrDefault(node => node.Id == _selectedId) ?? roots.FirstOrDefault();
            _tree.SelectedItem = selected;
            _selectedId = selected?.Id;
            _inspector.Refresh();
            if ((_inspector.ObjectId == null || graph.Object(_inspector.ObjectId) == null) && _selectedId != null)
            {
                _inspector.ShowObject(_selectedId);
            }

            _source.Text = new JsonAsset(data).ToJson();
            _source.SyntaxHighlighting = AssetSyntax.ForSource(name + ".json", "data_json", _source.Text);
            _status.Text = $"{Flatten(roots).Count()} hierarchy entries · IDs and references belong to this group" + (_write == null ? " · read-only" : " · property and group-source edits share asset undo");
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            _tree.ItemsSource = null;
            _inspector?.Dispose();
            _inspector = null;
            _details.Content = null;
            _status.Text = "Source cannot be inspected: " + error.Message;
        }
        finally { _refreshing = false; }
    }
    private static Entity Build(JsonNode? entry, SerializedObjectGraph graph, HashSet<string> visited, int depth)
    {
        var structure = entry as JsonObject;
        var id = structure?["id"]?.ToString() ?? (entry is JsonValue ? entry.ToString() : null);
        var label = id == null ? "Entity" : graph.Label(id);
        if (depth >= 64 || id != null && !visited.Add(id))
        {
            return new Entity(label + " (reference)", id, []);
        }

        var children = Values(structure?["children"]).Select(child => Build(child, graph, new HashSet<string>(visited), depth + 1)).ToList();
        foreach (var component in Values(structure?["components"]))
        {
            var key = component is JsonObject obj ? obj["id"]?.ToString() : component?.ToString();
            if (key != null)
            {
                children.Add(new Entity(graph.Label(key), key, []));
            }
        }
        var proto = id == null ? null : graph.Object(id)?["__PROTO"]?.ToString();
        if (proto != null)
        {
            label += " · prototype " + proto;
        }

        return new Entity(label, id, children);
    }
    private static IEnumerable<Entity> Flatten(IEnumerable<Entity> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
    private static IEnumerable<JsonNode?> Values(JsonNode? value) => value switch { JsonArray array => array, JsonObject obj => obj.Select(pair => pair.Value), _ => [] };
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TextDocument.Text) or nameof(ResourceDocument.Current) && TopLevel.GetTopLevel(this) != null && IsEffectivelyVisible)
        {
            Refresh();
        }
    }
    public void Dispose()
    {
        if (_document != null)
        {
            _document.PropertyChanged -= Changed;
        }

        _inspector?.Dispose();
    }
}
