using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class DeckPackEditorView : UserControl, IDisposable
{
    private readonly ResourceDocument _document;
    private readonly DeckPackAsset _pack;
    private readonly AssetEditorView _preview = new();
    private readonly ListBox _list = new() { Name = "PackDeckList" };
    private readonly TextBox _search = new() { Name = "PackDeckSearch", PlaceholderText = "Search decks…" };
    private readonly TextBlock _title = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private bool _filtering;
    public string? SelectedName
    {
        get; private set;
    }

    public DeckPackEditorView(ResourceDocument document, string? selected = null)
    {
        _document = document;
        _pack = (DeckPackAsset)document.Current;
        _list.ItemTemplate = new FuncDataTemplate<string>((name, _) => name == null ? new Control() : new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(4),
            Children =
            {
                new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = _pack.GetDeckType(name!), FontSize = 11, Foreground = Brushes.Gray }
            }
        });
        var navigation = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_search, Dock.Top);
        navigation.Children.Add(_search);
        navigation.Children.Add(_list);
        var content = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        content.Children.Add(_title);
        content.Children.Add(_preview);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,4,*") };
        grid.Children.Add(navigation);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetColumn(content, 2);
        grid.Children.Add(content);
        Content = grid;
        _list.SelectionChanged += (_, _) =>
        {
            if (!_filtering && _list.SelectedItem is string name && name != SelectedName)
            {
                SelectedName = name;
                _title.Text = name;
                _preview.ShowDocument(_document.OpenDeck(name));
            }
        };
        _search.TextChanged += (_, _) => Filter();
        _list.ItemsSource = _pack.DeckNames;
        _list.SelectedItem = selected != null && _pack.DeckNames.Contains(selected) ? selected : _pack.DeckNames.FirstOrDefault();
        if (_pack.DeckNames.Count == 0)
        {
            _title.Text = "This pack contains no decks.";
        }
    }

    private void Filter()
    {
        _filtering = true;
        try
        {
            _list.ItemsSource = _pack.DeckNames.Where(name => name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            _list.SelectedItem = _list.Items.Contains(SelectedName) ? SelectedName : null;
        }
        finally { _filtering = false; }
    }

    public void Dispose()
    {
        _preview.Dispose();
    }
}
