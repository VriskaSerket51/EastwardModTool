using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using EastwardLib.MetaData;
using EastwardModTool.Core.Navigation;

namespace EastwardModTool.Views;

public sealed class AssetBrowserView : UserControl
{
    private readonly TreeView _tree = new() { Name = "AssetTree" };
    private readonly TextBox _search = new() { Name = "AssetSearch", PlaceholderText = "Search assets…" };
    private readonly ComboBox _mode = new() { Name = "AssetViewMode", ItemsSource = new[] { "Gii directories", "Asset types" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _type = new() { Name = "AssetTypeFilter", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _count = new() { Margin = new Thickness(4), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
    private bool _updating;
    public AssetBrowser Browser { get; private set; } = new(new Dictionary<string, AssetInfo>());
    public event EventHandler? SelectionChanged;
    public AssetInfo? SelectedAsset => Browser.SelectedAsset;

    public AssetBrowserView()
    {
        _tree.ItemTemplate = new FuncTreeDataTemplate<AssetTreeNode>((node, _) =>
        {
            if (node == null)
            {
                return new Control();
            }

            var title = new TextBlock { Text = node.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            if (node.IsAsset && !node.IsMatch)
            {
                title.Foreground = Brushes.Gray;
            }

            var row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3), Children = { title } };
            if (node.IsAsset)
            {
                row.Children.Add(new TextBlock { Text = Browser.Mode == AssetBrowseMode.Types ? node.Asset!.AssetName : node.Asset!.Type, FontSize = 11, Foreground = Brush.Parse("#78B9B1"), TextTrimming = TextTrimming.CharacterEllipsis });
            }

            ToolTip.SetTip(row, node.Asset?.AssetName ?? node.Name);
            return row;
        }, node => node.Children);
        _tree.ItemContainerTheme = new ControlTheme(typeof(TreeViewItem))
        {
            BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(TreeViewItem))!,
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(AssetTreeNode.IsExpanded)) { Mode = BindingMode.TwoWay })
            }
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        var panel = new DockPanel { Margin = new Thickness(12) };
        var filters = new StackPanel { Spacing = 8, Children = { _mode, _search, _type, _count } };
        DockPanel.SetDock(filters, Dock.Top);
        panel.Children.Add(filters);
        panel.Children.Add(_tree);
        Content = panel;
        _mode.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();
        _type.SelectionChanged += (_, _) => Refresh();
        _tree.DoubleTapped += (_, _) =>
        {
            if (!_updating && _tree.SelectedItem is AssetTreeNode { IsAsset: true })
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _tree.SelectionChanged += (_, _) =>
        {
            if (_updating || _tree.SelectedItem is not AssetTreeNode { IsAsset: true } node)
            {
                return;
            }

            var previous = Browser.SelectedName;
            Browser.Select(node.Asset!.AssetName);
            UpdateCount();
            if (previous != Browser.SelectedName)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void SetAssets(IReadOnlyDictionary<string, AssetInfo> assets)
    {
        _updating = true;
        try
        {
            Browser = new AssetBrowser(assets);
            _type.ItemsSource = new[] { "All types" }.Concat(Browser.Types).ToArray();
            _type.SelectedIndex = 0;
            _search.Text = "";
        }
        finally { _updating = false; }
        Refresh();
    }

    public void SelectAsset(string name)
    {
        var previous = Browser.SelectedName;
        Browser.Select(name);
        RestoreSelection();
        UpdateCount();
        if (previous != name)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetBrowseMode(AssetBrowseMode mode)
    {
        _mode.SelectedIndex = mode == AssetBrowseMode.Types ? 1 : 0;
    }

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    private void Refresh()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            Browser.Update(_mode.SelectedIndex == 1 ? AssetBrowseMode.Types : AssetBrowseMode.Directories, _search.Text ?? "", _type.SelectedIndex > 0 ? _type.SelectedItem as string : null);
            _tree.ItemsSource = Browser.Roots;
            _tree.SelectedItem = Browser.SelectedNode;
            UpdateCount();
        }
        finally { _updating = false; }
    }

    private void RestoreSelection()
    {
        _updating = true;
        try
        {
            _tree.SelectedItem = Browser.SelectedNode;
        }
        finally { _updating = false; }
    }

    private void UpdateCount()
    {
        _count.Text = $"{Browser.MatchingCount:N0} assets" + (Browser.SelectedName != null && Browser.SelectedNode == null ? " · selection hidden by filter" : "");
    }
}
