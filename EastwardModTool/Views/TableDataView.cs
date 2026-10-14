using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

/// <summary>Virtualized rows show typed workbook data; source edits stay in the original document.</summary>
public sealed class TableDataView : UserControl, IDisposable
{
    private readonly TextDocument? _document;
    private readonly Func<IReadOnlyList<IReadOnlyList<string>>> _read;
    private readonly ListBox _rows = new() { Name = "TableRows" };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _sheets = new() { Name = "WorkbookSheets", MinWidth = 200, Margin = new Thickness(8) };
    public TableDataView(TextDocument document)
    {
        _document = document;
        if (document.Baseline is WorkbookAsset workbook)
        {
            _sheets.ItemsSource = workbook.Data!.AsObject().Select(pair => pair.Key).ToArray();
            _sheets.SelectedIndex = 0;
            _read = () =>
            {
                var edited = workbook.CompileData(JsonNode.Parse(document.Text)!);
                return edited.GetRows((string)_sheets.SelectedItem!).Select(row => (IReadOnlyList<string>)row.Select(cell => cell?.ToString() ?? "").ToArray()).ToArray();
            };
        }
        else if (document.Baseline is CsvAsset csv)
        {
            _sheets.IsVisible = false;
            _read = () => Rows(((JsonAsset)csv.CompileSource(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.Text)))).Data);
        }
        else
        {
            _sheets.IsVisible = false;
            _read = () => Rows(JsonNode.Parse(document.Text));
        }
        Setup();
        document.PropertyChanged += Changed;
    }
    public TableDataView(Func<IReadOnlyList<IReadOnlyList<string>>> read)
    {
        _read = read;
        _sheets.IsVisible = false;
        Setup();
    }
    private void Setup()
    {
        _rows.ItemTemplate = new FuncDataTemplate<IReadOnlyList<string>>((row, _) =>
        {
            if (row == null)
            {
                return new Control();
            }

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var cell in row)
            {
                panel.Children.Add(new TextBox { Text = cell, IsReadOnly = true, Width = 180, Margin = new Thickness(1), BorderThickness = new Thickness(0) });
            }

            return panel;
        });
        var panel = new DockPanel();
        DockPanel.SetDock(_sheets, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_sheets);
        panel.Children.Add(_status);
        panel.Children.Add(new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = panel;
        Loaded += (_, _) => Refresh();
        _sheets.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }
    public static IReadOnlyList<IReadOnlyList<string>> Rows(JsonNode? data)
    {
        if (data is JsonArray array && array.FirstOrDefault() is JsonObject)
        {
            var columns = array.OfType<JsonObject>().SelectMany(row => row.Select(pair => pair.Key)).Distinct(StringComparer.Ordinal).ToArray();
            return new[] { (IReadOnlyList<string>)columns }.Concat(array.Select(row => (IReadOnlyList<string>)columns.Select(column => row?[column]?.ToString() ?? "").ToArray())).ToArray();
        }
        if (data is JsonArray rows)
        {
            return rows.Select(row => (IReadOnlyList<string>)(row is JsonArray cells ? cells.Select(cell => cell?.ToString() ?? "").ToArray() : new[] { row?.ToString() ?? "" })).ToArray();
        }

        if (data is JsonObject obj)
        {
            return new[] { (IReadOnlyList<string>)new[] { "Key", "Value" } }.Concat(obj.Select(pair => (IReadOnlyList<string>)new[] { pair.Key, pair.Value?.ToString() ?? "" })).ToArray();
        }

        return [];
    }
    private void Refresh()
    {
        try
        {
            var rows = _read();
            _rows.ItemsSource = rows;
            _status.Text = $"{rows.Count:N0} rows · " + (_document?.CanEdit == true ? "edit through Source or Import" : "read-only preview");
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or NotSupportedException) { _rows.ItemsSource = null; _status.Text = error.Message; }
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
        if (_document != null)
        {
            _document.PropertyChanged -= Changed;
        }
    }
}
