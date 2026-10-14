using Avalonia.Controls;
using Avalonia.Layout;

namespace EastwardModTool.Views;

public enum WorkspaceLayout
{
    Wide,
    Split,
    Tabbed
}

/// <summary>Keep the same workspace controls and editing state while changing their arrangement.</summary>
public sealed class AdaptiveWorkspaceView : UserControl
{
    private readonly Control _assets;
    private readonly Control _editor;
    private readonly Control _metadata;
    private Grid? _grid;
    private TabControl? _tabs;
    private Control _active;
    public WorkspaceLayout Layout
    {
        get; private set;
    }

    public AdaptiveWorkspaceView(Control assets, Control editor, Control metadata)
    {
        _assets = assets;
        _editor = editor;
        _metadata = metadata;
        _active = assets;
        ArrangeWorkspace(WorkspaceLayout.Wide);
        SizeChanged += (_, args) =>
        {
            var layout = args.NewSize.Width >= 1120 ? WorkspaceLayout.Wide : args.NewSize.Width >= 800 ? WorkspaceLayout.Split : WorkspaceLayout.Tabbed;
            if (layout != Layout)
            {
                ArrangeWorkspace(layout);
            }
        };
    }

    public void ShowEditor()
    {
        _active = _editor;
        if (_tabs != null)
        {
            _tabs.SelectedIndex = Layout == WorkspaceLayout.Tabbed ? 1 : 0;
        }
    }

    public void ShowAssets()
    {
        _active = _assets;
        if (Layout == WorkspaceLayout.Tabbed && _tabs != null)
        {
            _tabs.SelectedIndex = 0;
        }
    }

    private void ArrangeWorkspace(WorkspaceLayout layout)
    {
        if (_tabs != null)
        {
            foreach (var item in _tabs.Items.OfType<TabItem>())
            {
                item.Content = null;
            }

            _tabs.Items.Clear();
        }
        _grid?.Children.Clear();
        Content = null;
        _tabs = null;
        _grid = null;
        Layout = layout;
        if (layout == WorkspaceLayout.Tabbed)
        {
            _tabs = new TabControl
            {
                Items =
                {
                    new TabItem { Header = "Assets", Content = _assets },
                    new TabItem { Header = "Editor", Content = _editor },
                    new TabItem { Header = "Metadata", Content = _metadata }
                },
                SelectedIndex = _active == _metadata ? 2 : _active == _editor ? 1 : 0
            };
            TrackSelection();
            Content = _tabs;
            return;
        }
        _grid = new Grid { ColumnDefinitions = new ColumnDefinitions(layout == WorkspaceLayout.Wide ? "280,4,*,4,260" : "240,4,*") };
        _grid.ColumnDefinitions[0].MinWidth = 180;
        _grid.ColumnDefinitions[2].MinWidth = 320;
        Add(_assets, 0);
        Add(new GridSplitter { ResizeDirection = GridResizeDirection.Columns }, 1);
        if (layout == WorkspaceLayout.Wide)
        {
            _grid.ColumnDefinitions[4].MinWidth = 220;
            Add(_editor, 2);
            Add(new GridSplitter { ResizeDirection = GridResizeDirection.Columns }, 3);
            Add(_metadata, 4);
        }
        else
        {
            _tabs = new TabControl
            {
                Items =
                {
                    new TabItem { Header = "Editor", Content = _editor },
                    new TabItem { Header = "Metadata", Content = _metadata }
                },
                SelectedIndex = _active == _metadata ? 1 : 0
            };
            TrackSelection();
            Add(_tabs, 2);
        }
        Content = _grid;
    }

    private void Add(Control control, int column)
    {
        Grid.SetColumn(control, column);
        _grid!.Children.Add(control);
    }

    private void TrackSelection()
    {
        _tabs!.SelectionChanged += (_, _) =>
        {
            if (_tabs?.SelectedItem is TabItem item && item.Content is Control selected)
            {
                _active = selected;
            }
        };
    }
}
