using System.ComponentModel;
using System.Text.Json;
using System.Text.Encodings.Web;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EastwardLib.Assets;
using EastwardLib.MetaData;
using EastwardLib.Modding;
using EastwardModTool.Core;
using EastwardModTool.Core.Documents;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class MainWindow : Window
{
    private readonly Grid _work = new();
    private readonly AssetBrowserView _assets = new();
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Text = "Open an Eastward installation" };
    private readonly TextBlock _status = new() { Text = "Ready", Margin = new Thickness(16, 8) };
    private readonly TextBox _metadata = new() { IsReadOnly = true, AcceptsReturn = true, FontSize = 12, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0) };
    private readonly AssetEditorView _editor = new();
    private readonly DocumentTabsView _tabs = new();
    private DocumentSession? _session;
    private readonly AdaptiveWorkspaceView _layout;
    private readonly List<(MenuItem Item, Func<bool> Available)> _commands = [];
    private readonly Menu _menu;
    private readonly MenuItem _pinned = new() { Header = "Pinned assets" };
    private readonly MenuItem _languagePatchDrafts = new() { Name = "LanguagePatchDrafts", Header = "Language patch drafts", IsEnabled = false };
    private readonly string? _decompilerPath;
    private readonly Func<IModManagerClient> _modManagerClient;
    private GameWorkspace? _workspace;
    private AssetDocument? _document;
    private bool _busy;
    private bool _closing;

    public MainWindow(string[] args, Func<IModManagerClient>? modManagerClient = null)
    {
        _modManagerClient = modManagerClient ?? (() => new LoaderModManagerClient());
        Title = "EastwardModTool";
        Width = 1400;
        Height = 860;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _decompilerPath = Option(args, "--decompiler");
        _menu = CreateMenu();
        var editorPanel = new DockPanel();
        var heading = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { _tabs, _title } };
        DockPanel.SetDock(heading, Dock.Top);
        editorPanel.Children.Add(heading);
        editorPanel.Children.Add(_editor);
        var inspector = new DockPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = "Asset metadata", FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 8) };
        DockPanel.SetDock(label, Dock.Top);
        inspector.Children.Add(label);
        inspector.Children.Add(_metadata);
        _title.TextWrapping = TextWrapping.Wrap;
        _title.MaxLines = 2;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxLines = 2;
        _layout = new AdaptiveWorkspaceView(_assets, editorPanel, inspector);
        _work.RowDefinitions = new RowDefinitions("Auto,*");
        _work.Children.Add(_menu);
        Grid.SetRow(_layout, 1);
        _work.Children.Add(_layout);
        var root = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(_work);
        Content = root;
        _assets.SelectionChanged += async (_, _) =>
        {
            if (_assets.SelectedAsset is AssetInfo info && !_busy && _document?.Name != info.AssetName)
            {
                await Run(async () =>
                {
                    var document = await Task.Run(() => _workspace!.OpenAsset(info.AssetName));
                    _session!.Open(document);
                });
            }
        };
        Closing += OnClosing;
        AddHandler(InputElement.KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        Closed += (_, _) => { _tabs.SetSession(null); _session?.Dispose(); _editor.Dispose(); _workspace?.Dispose(); };
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

    private Menu CreateMenu()
    {
        var modifier = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        bool Workspace() => _workspace != null;
        bool Document() => _document != null;
        bool Editable() => _document?.CanEdit == true;
        bool Changes() => _workspace?.HasChanges == true;
        Task Action(Action action)
        {
            action();
            return Task.CompletedTask;
        }
        var file = new MenuItem
        {
            Header = "_File",
            Items =
        {
            Command("Open game…", OpenGame, gesture: modifier + "+O"),
            Command("Open project…", OpenProject, gesture: modifier + "+Shift+O"),
            Command("Save project", SaveProject, Workspace, modifier + "+S"),
            new Separator(),
            Command("Close tab", () => Action(() => _session?.Close(_document!.Name)), Document, modifier + "+W"),
            Command("Reopen closed tab", () => Action(() => _session?.Reopen()), () => _session?.CanReopen == true, modifier + "+Shift+T", "ReopenDocument")
        }
        };
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Close();
        file.Items.Add(new Separator());
        file.Items.Add(exit);
        var edit = new MenuItem
        {
            Header = "_Edit",
            Items =
        {
            Command("Undo", () => Action(() => _document?.Undo()), () => _document?.CanUndo == true, modifier + "+Z"),
            Command("Redo", () => Action(() => _document?.Redo()), () => _document?.CanRedo == true, modifier + (OperatingSystem.IsMacOS() ? "+Shift+Z" : "+Y")),
            Command("Revert asset", () => Action(() => _document?.Reset()), () => _document?.IsDirty == true),
            new Separator(),
            Command("Find…", () => Action(() => FindText(false)), Document, modifier + "+F"),
            Command("Replace…", () => Action(() => FindText(true)), Editable, OperatingSystem.IsMacOS() ? "Meta+Alt+F" : "Ctrl+H")
        }
        };
        var asset = new MenuItem
        {
            Header = "_Asset",
            Items =
        {
            Command("Import file…", () => ImportSource(false), Editable),
            Command("Import folder…", () => ImportSource(true), Editable),
            Command("Export source…", ExportSource, () => _document != null && _document is not UnavailableDocument),
            new Separator(),
            new MenuItem { Header = "Global metadata", Items =
            {
                Command("Asset index", () => EditMetadata("@asset_index"), Workspace, name: "EditAssetIndex"),
                Command("Script library", () => EditMetadata("@script_library"), Workspace),
                Command("Texture index", () => EditMetadata("@texture_index"), Workspace)
            } }
        }
        };
        var mod = new MenuItem
        {
            Header = "_Mod",
            Items =
        {
            Command("Manage mods…", ManageMods, Workspace, name: "ManageModsMenu"),
            new Separator(),
            Command("Create language patch…", CreateLanguagePatch, Workspace, name: "CreateLanguagePatchMenu"),
            _languagePatchDrafts,
            new Separator(),
            Command("Export mod…", ExportMod, Changes),
            Command("Build archives…", BuildArchives, Changes),
            Command("Apply mod…", ApplyMod, Workspace)
        }
        };
        var view = new MenuItem
        {
            Header = "_View",
            Items =
        {
            Command("Back", () => Action(() => _session?.GoBack()), () => _session?.CanGoBack == true, "Alt+Left", "NavigateBack"),
            Command("Forward", () => Action(() => _session?.GoForward()), () => _session?.CanGoForward == true, "Alt+Right", "NavigateForward"),
            Command("Pin / unpin asset", () => Action(() => _session?.TogglePin(_document!.Name)), Document, name: "PinDocument"),
            _pinned,
            new Separator(),
            Command("Search assets", () => Action(() => { _layout.ShowAssets(); _assets.FocusSearch(); }), Workspace, modifier + "+L"),
            Command("Gii directories", () => Action(() => _assets.SetBrowseMode(EastwardModTool.Core.Navigation.AssetBrowseMode.Directories)), Workspace),
            Command("Asset types", () => Action(() => _assets.SetBrowseMode(EastwardModTool.Core.Navigation.AssetBrowseMode.Types)), Workspace)
        }
        };
        return new Menu { Name = "MainMenu", Items = { file, edit, asset, mod, view } };
    }

    private MenuItem Command(string text, Func<Task> action, Func<bool>? available = null, string? gesture = null, string? name = null)
    {
        var item = new MenuItem { Header = text, Name = name, InputGesture = gesture == null ? null : KeyGesture.Parse(gesture) };
        item.Click += async (_, _) => await Run(action);
        _commands.Add((item, available ?? (() => true)));
        return item;
    }

    private void FindText(bool replace)
    {
        _layout.ShowEditor();
        if (!_editor.Find(replace))
        {
            _layout.ShowAssets();
            _assets.FocusSearch();
        }
    }

    private async void OnShortcut(object? sender, KeyEventArgs args)
    {
        if (_busy)
        {
            return;
        }
        if (args.KeyModifiers == KeyModifiers.Alt && args.Key is Key.Left or Key.Right)
        {
            if (args.Key == Key.Left)
            {
                _session?.GoBack();
            }
            else
            {
                _session?.GoForward();
            }

            args.Handled = true;
            return;
        }
        if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            return;
        }

        if (args.Key == Key.W)
        {
            if (_document != null)
            {
                _session?.Close(_document.Name);
            }

            args.Handled = true;
            return;
        }
        if (args.Key == Key.T && args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _session?.Reopen();
            args.Handled = true;
            return;
        }
        if (args.Key is Key.F or Key.H)
        {
            if (OperatingSystem.IsMacOS() && args.Key == Key.H && args.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                return;
            }

            _layout.ShowEditor();
            if (!_editor.Find(args.Key == Key.H || args.KeyModifiers.HasFlag(KeyModifiers.Alt)))
            {
                _layout.ShowAssets();
                _assets.FocusSearch();
            }
            args.Handled = true;
            return;
        }
        if (args.Key == Key.L)
        {
            _layout.ShowAssets();
            _assets.FocusSearch();
            args.Handled = true;
            return;
        }
        Func<Task>? action = args.Key switch
        {
            Key.S => SaveProject,
            Key.O => args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? OpenProject : OpenGame,
            _ => null
        };
        if (action == null)
        {
            return;
        }

        args.Handled = true;
        await Run(action);
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _status.Text = "Working…";
        try
        {
            var operation = action();
            if (!operation.IsCompleted)
            {
                _work.IsEnabled = false;
            }

            await operation;
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

        _tabs.SetSession(null);
        _languagePatchDrafts.Items.Clear();
        _session?.Dispose();
        _editor.Clear();
        if (_workspace != null)
        {
            _workspace.Changed -= WorkspaceChanged;
        }

        _workspace?.Dispose();
        _workspace = workspace;
        _session = new DocumentSession();
        _session.SelectionChanged += (_, _) => ShowSelectedDocument();
        _session.Changed += (_, _) => UpdateState();
        _tabs.SetSession(_session);
        _document = null;
        workspace.Changed += WorkspaceChanged;
        _assets.SetAssets(workspace.Assets);
        if (workspace.Documents.Any(document => document is RuntimeResourcesDocument || document is TextDocument text && text.Text.StartsWith("-- EastwardModTool.LanguagePatch.v1", StringComparison.Ordinal)))
        {
            SetLanguagePatchDrafts(workspace.Documents.Where(document => document.IsDirty));
        }
        _title.Text = "Select an asset";
        _layout.ShowAssets();
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

        _session!.Open(_workspace.OpenAsset(name));
        return Task.CompletedTask;
    }

    private void ShowSelectedDocument()
    {
        if (_document != null)
        {
            _document.PropertyChanged -= DocumentChanged;
        }

        _document = _session?.SelectedDocument;
        if (_document == null)
        {
            _editor.Dispose();
            _layout.ShowAssets();
            _assets.FocusSearch();
            _title.Text = "Select an asset";
            _metadata.Text = "";
            _status.Text = "Tabs closed · editing drafts stay in the project.";
        }
        else
        {
            _document.PropertyChanged += DocumentChanged;
            _title.Text = _document.Name.TrimStart('@');
            if (_workspace!.Assets.TryGetValue(_document.Name, out var info))
            {
                _assets.SelectAsset(info.AssetName);
                _metadata.Text = info.Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            else
            {
                _metadata.Text = "Global metadata source";
            }

            _editor.ShowDocument(_document);
            _layout.ShowEditor();
            _status.Text = _document is MetadataDocument ? "Metadata changes are validated when you build a mod." : _document.CanEdit ? "Edit the source, then export a mod or build archives." : _document is UnavailableDocument ? "Preview unavailable · see the resource notice." : "Read-only preview · use the parent package to edit virtual assets.";
        }
        UpdateState();
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

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AssetDocument.IsDirty) or nameof(AssetDocument.CanUndo) or nameof(AssetDocument.CanRedo))
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        foreach (var (item, available) in _commands)
        {
            item.IsEnabled = !_busy && available();
        }

        var pinned = _session?.PinnedNames.ToArray() ?? [];
        if (!_pinned.Items.OfType<MenuItem>().Select(item => (string)item.Header!).SequenceEqual(pinned))
        {
            _pinned.Items.Clear();
            foreach (var name in pinned)
            {
                var item = new MenuItem { Header = name };
                item.Click += async (_, _) => await Run(() => { _session!.Select(name); return Task.CompletedTask; });
                _pinned.Items.Add(item);
            }
        }
        _pinned.IsEnabled = !_busy && pinned.Length > 0;
        _languagePatchDrafts.IsEnabled = !_busy && _languagePatchDrafts.Items.Count > 0;
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

    private async Task ManageMods()
    {
        if (_workspace == null)
        {
            return;
        }

        await new ModManagerView(_workspace.GameDirectory, _modManagerClient()).ShowDialog(this);
        _status.Text = "Mod management closed · project editing drafts are unchanged";
    }

    private async Task CreateLanguagePatch()
    {
        if (_workspace == null)
        {
            return;
        }

        var documents = await new LanguagePatchView(_workspace).ShowDialog<IReadOnlyList<AssetDocument>?>(this);
        if (documents == null || documents.Count == 0)
        {
            return;
        }

        SetLanguagePatchDrafts(_workspace.Documents.Where(document => document.IsDirty).Concat(documents).DistinctBy(document => document.Name));
        _session!.Open(documents.FirstOrDefault(document => document is TextDocument) ?? documents[0]);
        _status.Text = $"Created {documents.Count} language-patch asset drafts · review and save the project, then export a mod";
    }

    private void SetLanguagePatchDrafts(IEnumerable<AssetDocument> documents)
    {
        _languagePatchDrafts.Items.Clear();
        foreach (var document in documents)
        {
            var item = new MenuItem { Header = document.Name };
            item.Click += async (_, _) => await Run(() => { _session!.Open(document); return Task.CompletedTask; });
            _languagePatchDrafts.Items.Add(item);
        }
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
