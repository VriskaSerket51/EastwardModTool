using System.Text.Json.Nodes;
using System.ComponentModel;
using EastwardModTool.Core.Documents;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using EastwardLib.Assets;

namespace EastwardModTool.Views;

public sealed class SceneHierarchyView : UserControl, IDisposable
{
    public sealed record Entity(string Label, JsonNode? Data, IReadOnlyList<Entity> Children);
    private readonly ComboBox _groups = new() { Name = "SceneGroups", MinWidth = 180, Margin = new Thickness(8) };
    private readonly TreeView _tree = new() { Name = "SceneHierarchy" };
    private readonly ContentControl _details = new();
    private IReadOnlyDictionary<string, JsonNode?> _data;
    private readonly TextDocument? _document;
    public SceneHierarchyView(IReadOnlyDictionary<string, JsonNode?> data)
    {
        _data = data;
        _groups.ItemsSource = data.Keys.ToArray();
        _groups.SelectedIndex = data.Count > 1 ? Math.Max(1, Array.FindIndex(data.Keys.ToArray(), name => name.Contains("default", StringComparison.OrdinalIgnoreCase))) : data.Count == 0 ? -1 : 0;
        _tree.ItemTemplate = new FuncTreeDataTemplate<Entity>((node, _) => new TextBlock { Text = node?.Label, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }, node => node.Children);
        var panel = new DockPanel();
        DockPanel.SetDock(_groups, Dock.Top);
        panel.Children.Add(_groups);
        var tabs = new TabControl { Items = { new TabItem { Header = "Hierarchy", Content = _tree }, new TabItem { Header = "Selected object", Content = _details } } };
        panel.Children.Add(tabs);
        Content = panel;
        _groups.SelectionChanged += (_, _) => Refresh();
        _tree.SelectionChanged += (_, _) => { if (_tree.SelectedItem is Entity node) { _details.Content = new JsonDataView(() => node.Data); } };
        Refresh();
    }
    public SceneHierarchyView(TextDocument document) : this(new Dictionary<string, JsonNode?> { ["Entities"] = JsonNode.Parse(document.Text) })
    {
        _document = document;
        document.PropertyChanged += Changed;
        Loaded += (_, _) => RefreshDocument();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TextDocument.Text) && TopLevel.GetTopLevel(this) != null && IsEffectivelyVisible)
        {
            RefreshDocument();
        }
    }
    private void RefreshDocument()
    {
        try
        {
            _data = new Dictionary<string, JsonNode?> { ["Entities"] = JsonNode.Parse(_document!.Text) };
            Refresh();
        }
        catch (System.Text.Json.JsonException) { _tree.ItemsSource = null; }
    }
    public void Dispose()
    {
        if (_document != null)
        {
            _document.PropertyChanged -= Changed;
        }
    }
    private void Refresh()
    {
        if (_groups.SelectedItem is not string name)
        {
            return;
        }

        var data = _data[name];
        var map = data?["map"] as JsonObject;
        Entity Build(JsonNode? entry, HashSet<string> visited, int depth)
        {
            var id = entry is JsonObject ? entry["id"]?.ToString() : entry?.ToString();
            var body = id == null ? entry : map?[id]?["body"] ?? entry;
            var label = body?["_fullname"]?.ToString() ?? body?["name"]?.ToString() ?? id ?? "Entity";
            if (depth > 32 || id != null && !visited.Add(id))
            {
                return new Entity(label + " (reference)", body, []);
            }

            var children = Values(entry is JsonObject ? entry["children"] : null).Select(child => Build(child, new HashSet<string>(visited), depth + 1)).ToList();
            foreach (var component in Values(entry is JsonObject ? entry["components"] : null))
            {
                var key = component?.ToString() ?? "";
                children.Add(new Entity(map?[key]?["model"]?.ToString() ?? key, map?[key]?["body"], []));
            }
            return new Entity(label, body, children);
        }
        var entities = Values(data?["entities"]).Select(entry => Build(entry, [], 0)).ToArray();
        _tree.ItemsSource = entities;
        _details.Content = new JsonDataView(() => data);
        if (entities.Length == 0)
        {
            _tree.ItemsSource = new[] { new Entity("Scene configuration", data, []) };
        }
    }
    private static IEnumerable<JsonNode?> Values(JsonNode? value)
    {
        return value switch
        {
            JsonArray array => array,
            JsonObject obj => obj.Select(pair => pair.Value),
            _ => []
        };
    }
}
