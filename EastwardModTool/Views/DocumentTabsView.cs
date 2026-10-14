using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class DocumentTabsView : UserControl
{
    private readonly Dictionary<string, StackPanel> _rows = new(StringComparer.Ordinal);
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private DocumentSession? _session;

    public DocumentTabsView()
    {
        Content = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetSession(null);
    }

    public void SetSession(DocumentSession? session)
    {
        if (_session != null)
        {
            _session.Changed -= SessionChanged;
        }

        _tabs.Children.Clear();
        _rows.Clear();
        _session = session;
        if (session != null)
        {
            session.Changed += SessionChanged;
        }

        Refresh();
    }

    private void SessionChanged(object? sender, EventArgs args)
    {
        Refresh();
    }

    private void Refresh()
    {
        var openNames = _session?.Documents.Select(document => document.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var name in _rows.Keys.Where(name => !openNames.Contains(name)).ToArray())
        {
            _tabs.Children.Remove(_rows[name]);
            _rows.Remove(name);
        }
        if (_session != null)
        {
            foreach (var document in _session.Documents)
            {
                var caption = Path.GetFileName(document.Name).TrimStart('@') + (document.IsDirty ? " •" : "");
                if (_rows.TryGetValue(document.Name, out var existing))
                {
                    var button = (Button)existing.Children[0];
                    ((TextBlock)button.Content!).Text = caption;
                    var active = ReferenceEquals(document, _session.SelectedDocument);
                    if (active && button.FontWeight != FontWeight.Bold)
                    {
                        button.BringIntoView();
                    }

                    button.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
                    continue;
                }
                var title = new TextBlock { Text = caption, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
                var select = new Button { Content = title, Name = "DocumentTab", Padding = new Thickness(10, 6), FontWeight = ReferenceEquals(document, _session.SelectedDocument) ? FontWeight.Bold : FontWeight.Normal };
                ToolTip.SetTip(select, document.Name);
                if (ReferenceEquals(document, _session.SelectedDocument))
                {
                    select.Loaded += (_, _) => select.BringIntoView();
                }

                select.Click += (_, _) => _session.Select(document.Name);
                var close = new Button { Content = "×", Name = "CloseDocument", Padding = new Thickness(8, 6) };
                ToolTip.SetTip(close, "Close tab · edits stay in the project");
                close.Click += (_, _) => _session.Close(document.Name);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { select, close } };
                _rows.Add(document.Name, row);
                _tabs.Children.Add(row);
            }
        }
    }
}
