using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EastwardLib.Assets;
using EastwardLib.MetaData;
using EastwardLib.Modding;
using EastwardModTool.Core;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class MainWindow : Window
{
    private readonly Grid _work = new();
    private readonly ListBox _assets = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search assets…" };
    private readonly ComboBox _type = new() { MinWidth = 140 };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Text = "Open an Eastward installation" };
    private readonly TextBlock _status = new() { Text = "Ready", Margin = new Thickness(16, 8) };
    private readonly TextBlock _count = new() { Margin = new Thickness(8), Foreground = Brushes.Gray };
    private readonly TextBox _metadata = new() { IsReadOnly = true, AcceptsReturn = true, FontSize = 12, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0) };
    private readonly AssetEditorView _editor = new();
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly string? _decompilerPath;
    private GameWorkspace? _workspace;
    private AssetDocument? _document;
    private bool _busy;
    private bool _closing;

    public MainWindow(string[] args)
    {
        Title = "EastwardModTool";
        Width = 1400;
        Height = 860;
        MinWidth = 980;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _decompilerPath = Option(args, "--decompiler");
        _undo = Button("Undo", () => { _document?.Undo(); return Task.CompletedTask; });
        _redo = Button("Redo", () => { _document?.Redo(); return Task.CompletedTask; });
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(16, 12),
            Children =
            {
                Button("Open game", OpenGame), Button("Open project", OpenProject), Button("Save project", SaveProject), Button("Export mod", ExportMod), Button("Build archives", BuildArchives), Button("Apply mod", ApplyMod), _undo, _redo
            }
        };
        var sidebar = new DockPanel { Margin = new Thickness(12) };
        var filters = new StackPanel { Spacing = 8, Children = { _search, _type, _count } };
        DockPanel.SetDock(filters, Dock.Top);
        sidebar.Children.Add(filters);
        sidebar.Children.Add(_assets);
        _assets.ItemTemplate = new FuncDataTemplate<AssetInfo>((info, _) => info == null ? new Control() : new StackPanel
        {
            Margin = new Thickness(4, 5),
            Children =
            {
                new TextBlock { Text = info!.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = info.AssetName, FontSize = 11, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = info.Type, FontSize = 11, Foreground = Brush.Parse("#78B9B1") }
            }
        });
        var editorPanel = new DockPanel();
        var heading = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { _title } };
        heading.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button("Import file", () => ImportSource(false)), Button("Import folder", () => ImportSource(true)), Button("Export source", ExportSource), Button("Revert", () => { _document?.Reset(); return Task.CompletedTask; })
            }
        });
        DockPanel.SetDock(heading, Dock.Top);
        editorPanel.Children.Add(heading);
        editorPanel.Children.Add(_editor);
        var inspector = new DockPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = "Asset metadata", FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 8) };
        DockPanel.SetDock(label, Dock.Top);
        inspector.Children.Add(label);
        inspector.Children.Add(_metadata);
        var metadataActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                Button("Index", () => EditMetadata("@asset_index")),
                Button("Scripts", () => EditMetadata("@script_library")),
                Button("Textures", () => EditMetadata("@texture_index"))
            }
        };
        DockPanel.SetDock(metadataActions, Dock.Top);
        inspector.Children.Insert(1, metadataActions);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("320,4,*,4,280") };
        Add(content, sidebar, 0);
        Add(content, new GridSplitter { ResizeDirection = GridResizeDirection.Columns }, 1);
        Add(content, editorPanel, 2);
        Add(content, new GridSplitter { ResizeDirection = GridResizeDirection.Columns }, 3);
        Add(content, inspector, 4);
        _work.RowDefinitions = new RowDefinitions("Auto,*");
        _work.Children.Add(toolbar);
        Grid.SetRow(content, 1);
        _work.Children.Add(content);
        var root = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(_work);
        Content = root;
        _search.TextChanged += (_, _) => FilterAssets();
        _type.SelectionChanged += (_, _) => FilterAssets();
        _assets.SelectionChanged += async (_, _) =>
        {
            if (_assets.SelectedItem is AssetInfo info && !_busy)
            {
                await Run(async () =>
                {
                    var document = await Task.Run(() => _workspace!.OpenAsset(info.AssetName));
                    if (_document != null)
                    {
                        _document.PropertyChanged -= DocumentChanged;
                    }

                    _document = document;
                    document.PropertyChanged += DocumentChanged;
                    _title.Text = info.AssetName;
                    _metadata.Text = info.Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    _editor.ShowDocument(document);
                    UpdateState();
                    _status.Text = document.CanEdit ? "Edit the source, then export a mod or build archives." : "Virtual asset · edit its parent source package.";
                });
            }
        };
        Closing += OnClosing;
        Closed += (_, _) => { _editor.Dispose(); _workspace?.Dispose(); };
        UpdateState();
        var game = Option(args, "--game");
        if (game != null)
        {
            Opened += async (_, _) => await Run(() => LoadGame(game, Option(args, "--overlay")));
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Add(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7) };
        button.Click += async (_, _) => await Run(action);
        return button;
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _work.IsEnabled = false;
        _status.Text = "Working…";
        try
        {
            await action();
        }
        catch (Exception error) { _status.Text = error.Message; System.Diagnostics.Trace.WriteLine(error); }
        finally { _busy = false; _work.IsEnabled = true; UpdateState(); }
    }

    private async Task<string?> Folder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> File(string title, bool save, string filename = "example.ewmod")
    {
        if (save)
        {
            var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = filename, ShowOverwritePrompt = true });
            return result?.TryGetLocalPath();
        }
        var results = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return results.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task OpenGame()
    {
        if (_workspace?.IsModified == true && !await ConfirmDiscard())
        {
            return;
        }

        var path = await Folder("Select Eastward or its content/game folder");
        if (path != null)
        {
            await LoadGame(path, null);
        }
    }

    private async Task LoadGame(string path, string? overlay)
    {
        var workspace = await Task.Run(() => GameWorkspace.Open(path, overlay, _decompilerPath));
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        SetWorkspace(workspace);
    }

    private void SetWorkspace(GameWorkspace workspace)
    {
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        _editor.Dispose();
        if (_workspace != null)
        {
            _workspace.Changed -= WorkspaceChanged;
        }

        _workspace?.Dispose();
        _workspace = workspace;
        _document = null;
        workspace.Changed += WorkspaceChanged;
        _type.ItemsSource = new[] { "All types" }.Concat(workspace.Assets.Values.Select(info => info.Type).Distinct().Order(StringComparer.Ordinal)).ToArray();
        _type.SelectedIndex = 0;
        FilterAssets();
        _title.Text = "Select an asset";
        _metadata.Text = "";
        _status.Text = $"{workspace.Assets.Count:N0} indexed assets · archives load on demand";
    }

    private void WorkspaceChanged(object? sender, EventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateState);
    }

    private async Task OpenProject()
    {
        if (_workspace?.IsModified == true && !await ConfirmDiscard())
        {
            return;
        }

        var path = await File("Open editing project", false);
        if (path == null)
        {
            return;
        }

        var workspace = await Task.Run(() => WorkspaceProject.Open(path, decompilerPath: _decompilerPath));
        SetWorkspace(workspace);
        _status.Text = "Opened " + path;
    }

    private Task EditMetadata(string name)
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("Open the game first");
        }

        var document = _workspace.OpenAsset(name);
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        _document = document;
        document.PropertyChanged += DocumentChanged;
        _title.Text = name.TrimStart('@');
        _metadata.Text = "Global metadata source";
        _editor.ShowDocument(document);
        UpdateState();
        _status.Text = "Metadata changes are validated when you build a mod.";
        return Task.CompletedTask;
    }

    private async Task SaveProject()
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("Open the game first");
        }

        var path = _workspace.ProjectPath ?? await File("Save editing project", true, "project.ewproject");
        if (path == null)
        {
            return;
        }

        await Task.Run(() => WorkspaceProject.Save(_workspace, path));
        _status.Text = "Saved " + path;
    }

    private void FilterAssets()
    {
        if (_workspace == null)
        {
            return;
        }

        var search = _search.Text ?? "";
        var type = _type.SelectedItem as string;
        var values = _workspace.Assets.Values.Where(info => info.Type != "folder" && (type == null || type == "All types" || info.Type == type) && info.AssetName.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(info => info.AssetName, StringComparer.Ordinal).ToArray();
        _assets.ItemsSource = values;
        _count.Text = $"{values.Length:N0} assets";
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AssetDocument.IsDirty) or nameof(AssetDocument.CanUndo) or nameof(AssetDocument.CanRedo))
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        _undo.IsEnabled = _document?.CanUndo == true;
        _redo.IsEnabled = _document?.CanRedo == true;
        Title = "EastwardModTool" + (_workspace?.IsModified == true ? " • Unsaved changes" : "");
    }

    private async Task ImportSource(bool folder)
    {
        if (_document == null)
        {
            throw new InvalidOperationException("Select an asset first");
        }

        var path = folder ? await Folder("Select the edited source package") : await File("Select an edited source file", false);
        if (path != null)
        {
            var document = _document;
            var asset = await Task.Run(() => document.ReadSource(path));
            document.ImportAsset(asset);
            _status.Text = "Imported " + path;
        }
    }

    private async Task ExportSource()
    {
        if (_document == null)
        {
            throw new InvalidOperationException("Select an asset first");
        }

        var isFolder = _document is ResourceDocument resource && resource.Current is PackageAsset or MSpriteAsset or LocalePackAsset && resource.Current is not (TexturePackAsset or TilesetPackAsset);
        var filename = Path.GetFileName(_document.Name);
        if (_document is ResourceDocument imagePackage && imagePackage.Current is TexturePackAsset or TilesetPackAsset)
        {
            filename = Path.ChangeExtension(filename, ".psd");
        }

        var path = isFolder ? await Folder("Select a parent directory for exported source") : await File("Export source", true, filename);
        if (path == null)
        {
            return;
        }

        if (isFolder)
        {
            path = Path.Combine(path, filename + "-source");
        }

        var output = path;
        await Task.Run(() => _document.ExportSource(output));
        _status.Text = "Exported " + output;
    }

    private async Task ExportMod()
    {
        if (_workspace == null || !_workspace.HasChanges)
        {
            throw new InvalidOperationException("Edit at least one asset first");
        }

        var path = await File("Export mod package", true);
        if (path != null)
        {
            await Task.Run(() => _workspace.BuildMod(Path.GetFileNameWithoutExtension(path)).SaveTo(path, true));
            _status.Text = "Saved " + path;
        }
    }

    private async Task BuildArchives()
    {
        if (_workspace == null || !_workspace.HasChanges)
        {
            throw new InvalidOperationException("Edit at least one asset first");
        }

        var parent = await Folder("Select a parent directory for new archives");
        if (parent == null)
        {
            return;
        }

        var path = Path.Combine(parent, "eastward-mod-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        await Task.Run(() => _workspace.SaveArchives(path));
        _status.Text = "Built " + path;
    }

    private async Task ApplyMod()
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("Open the original game first");
        }

        var source = await File("Open mod package", false);
        if (source == null)
        {
            return;
        }

        var mod = await Task.Run(() => ModPackage.Read(source));
        var conflicts = await Task.Run(() => _workspace.ValidateMod(mod));
        if (conflicts.Count > 0)
        {
            throw new InvalidDataException("Incompatible resources: " + string.Join(", ", conflicts));
        }

        var parent = await Folder("Select a parent directory for modded archives");
        if (parent == null)
        {
            return;
        }

        var output = Path.Combine(parent, "eastward-mod-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        await Task.Run(() => _workspace.ApplyMod(mod, output));
        _status.Text = "Applied " + mod.Name + " to " + output;
    }

    private async Task<bool> ConfirmDiscard()
    {
        var dialog = new Window { Title = "Unsaved changes", Width = 420, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var discard = new Button { Content = "Discard changes" };
        var cancel = new Button { Content = "Cancel" };
        discard.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { new TextBlock { Text = "Discard the edits in this workspace?" }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, discard } } } };
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        if (_busy)
        {
            args.Cancel = true;
            return;
        }
        if (_workspace?.IsModified == true)
        {
            args.Cancel = true;
            if (await ConfirmDiscard())
            {
                _closing = true;
                Close();
            }
        }
    }
}
