using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class PackageBrowserView : UserControl, IDisposable
{
    private readonly AssetEditorView _preview = new();
    private readonly ListBox _list = new() { Name = "PackageFiles" };
    public string? SelectedName => _list.SelectedItem as string;
    public PackageBrowserView(ResourceDocument document, string? selected = null)
    {
        _list.ItemsSource = document.Entries;
        _list.ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is string name) { _preview.ShowDocument(document.OpenItem(name)); } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        _list.Margin = new Thickness(8);
        grid.Children.Add(_list);
        Grid.SetColumn(_preview, 1);
        grid.Children.Add(_preview);
        Content = grid;
        _list.SelectedItem = selected != null && document.Entries.Contains(selected) ? selected : document.Entries.FirstOrDefault();
    }
    public void Dispose()
    {
        _preview.Dispose();
    }
}
