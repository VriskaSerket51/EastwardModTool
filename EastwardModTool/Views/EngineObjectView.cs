using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using EastwardModTool.Core.Documents;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class EngineObjectView : UserControl, IDisposable
{
    public sealed record ObjectEntry(string Id, string Label);
    private readonly TextDocument _document;
    private readonly ComboBox _objects = new() { Name = "EngineObjects", Margin = new Thickness(8), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { Name = "EngineObjectSummary", Margin = new Thickness(8), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly JsonDataView _properties;
    private bool _refreshing;
    public EngineObjectView(TextDocument document)
    {
        _document = document;
        _properties = new JsonDataView(document);
        _objects.ItemTemplate = new FuncDataTemplate<ObjectEntry>((entry, _) => new TextBlock { Text = entry?.Label, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        _objects.SelectionChanged += (_, _) => { if (!_refreshing && _objects.SelectedItem is ObjectEntry entry) { _properties.ShowObject(entry.Id); } };
        var panel = new DockPanel();
        DockPanel.SetDock(_objects, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_objects);
        panel.Children.Add(_status);
        panel.Children.Add(_properties);
        Content = panel;
        document.PropertyChanged += Changed;
        Loaded += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var graph = new SerializedObjectGraph(JsonNode.Parse(_document.Text));
            var selected = (_objects.SelectedItem as ObjectEntry)?.Id ?? graph.RootId;
            var entries = graph.Objects?.Select(pair => new ObjectEntry(pair.Key, graph.Label(pair.Key) + " · " + pair.Key)).ToArray() ?? [];
            _objects.ItemsSource = entries;
            _objects.SelectedItem = entries.FirstOrDefault(entry => entry.Id == selected) ?? entries.FirstOrDefault();
            if (_properties.ObjectId == null && _objects.SelectedItem is ObjectEntry entry)
            {
                _properties.ShowObject(entry.Id);
            }

            _status.Text = $"{entries.Length} serialized objects · model names and local references are preserved";
        }
        catch (System.Text.Json.JsonException error) { _objects.ItemsSource = null; _status.Text = "Invalid Source: " + error.Message; }
        finally { _refreshing = false; }
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
        _properties.Dispose();
    }
}
