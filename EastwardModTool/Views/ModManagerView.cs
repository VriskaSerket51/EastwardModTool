using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EastwardModTool.Editing;

namespace EastwardModTool.Views;

public sealed class ModManagerView : Window
{
    private sealed record ModRow(ManagedMod Mod, CheckBox Enabled, Button Remove);
    private readonly IModManagerClient _client;
    private readonly TextBox _directory = new() { Name = "ModInstallation", PlaceholderText = "Eastward installation folder" };
    private readonly TextBlock _pending = new() { Name = "ModActivationPreview", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 2) };
    private readonly TextBlock _installed = new() { Name = "LoaderInstalledStatus", Text = "Installation not inspected", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "ModManagerStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 8) };
    private readonly StackPanel _mods = new() { Name = "ManagedModRows", Spacing = 10 };
    private readonly List<ModRow> _rows = [];
    private readonly Button _refresh = new() { Name = "RefreshMods", Content = "Refresh" };
    private readonly Button _install = new() { Name = "InstallLoader", Content = "Install / update loader" };
    private readonly Button _uninstall = new() { Name = "UninstallLoader", Content = "Uninstall loader" };
    private readonly Button _addFile = new() { Name = "AddModFile", Content = "Add .ewmod / Lua file…" };
    private readonly Button _addFolder = new() { Name = "AddModFolder", Content = "Add Lua mod folder…" };
    private readonly Button _apply = new() { Name = "ApplyModActivation", Content = "Apply enabled mods" };
    private readonly Button _close = new() { Content = "Close" };
    private readonly Button _browse = new() { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
    private ModManagerSnapshot? _snapshot;
    private string? _loadedDirectory;
    private bool _busy;
    public ModManagerView(string directory, IModManagerClient client)
    {
        _client = client;
        _directory.Text = directory;
        Title = "Manage Eastward mods";
        Width = 860;
        Height = 740;
        MinWidth = 440;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var top = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        top.Children.Add(new TextBlock { Text = "Game installation", FontWeight = FontWeight.SemiBold });
        var path = new DockPanel();
        DockPanel.SetDock(_browse, Dock.Right);
        path.Children.Add(_browse);
        path.Children.Add(_directory);
        top.Children.Add(path);
        top.Children.Add(_installed);
        top.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { _refresh, _install, _uninstall } });
        top.Children.Add(new TextBlock { Text = "Installation and activation validate managed files and resource conflicts before writing. Baseline backups support restoring the loader changes.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray });
        top.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { _addFile, _addFolder } });
        top.Children.Add(new TextBlock { Text = "Enable or disable mods below, then Apply. Refresh reads the installation without saving pending checkbox changes.", TextWrapping = TextWrapping.Wrap });
        foreach (var button in new[] { _refresh, _install, _uninstall, _addFile, _addFolder })
        {
            button.Margin = new Thickness(0, 0, 8, 6);
        }

        var footer = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 4), Children = { _apply, _close } };
        _apply.Margin = new Thickness(0, 0, 8, 0);
        var panel = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        var status = new ScrollViewer { Content = _status, MaxHeight = 140, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(_pending, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(status);
        panel.Children.Add(_pending);
        var scroll = new StackPanel { Children = { top, _mods } };
        _mods.Margin = new Thickness(16, 0);
        panel.Children.Add(new ScrollViewer { Name = "ModManagerScroll", Content = scroll, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = panel;
        _directory.TextChanged += (_, _) => UpdateState();
        _browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Eastward installation", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string root)
            {
                _directory.Text = root;
                await Refresh();
            }
        };
        _refresh.Click += async (_, _) => await Refresh();
        _install.Click += async (_, _) => await Run(root => _client.Install(root), "Loader installed / updated");
        _uninstall.Click += async (_, _) => await Run(root => _client.Uninstall(root), "Loader uninstalled");
        _addFile.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select mod package or Lua file", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Eastward mods") { Patterns = ["*.ewmod", "*.lua"] }, FilePickerFileTypes.All] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is string source)
            {
                await AddSource(source);
            }
        };
        _addFolder.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Lua mod folder", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string source)
            {
                await AddSource(source);
            }
        };
        _apply.Click += async (_, _) =>
        {
            try
            {
                var values = _rows.ToDictionary(row => row.Mod.Id, row => row.Enabled.IsChecked == true, StringComparer.Ordinal);
                await Run(root => _client.Apply(root, values), "Mod activation saved");
            }
            catch (Exception error) { _status.Text = error.Message; }
        };
        _close.Click += (_, _) => Close();
        Closing += (_, args) => args.Cancel = _busy;
        Opened += async (_, _) => await Refresh();
        UpdateState();
    }
    public Task AddSource(string source) => Run(root => _client.Add(root, source), "Mod added");
    private string DirectoryPath()
    {
        var path = _directory.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Choose an absolute game installation path.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private async Task Refresh() => await Run(null, "Installation inspected");
    private async Task Run(Action<string>? action, string message)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateState();
        _status.Text = "Working…";
        try
        {
            var root = DirectoryPath();
            var state = await Task.Run(() => { action?.Invoke(root); return _client.Inspect(root); });
            _snapshot = state;
            _loadedDirectory = state.GameDirectory ?? root;
            if (state.GameDirectory != null)
            {
                _directory.Text = state.GameDirectory;
            }

            ShowMods(state);
            _installed.Text = state.Installed ? "Loader installed" + (state.Version == null ? "" : " · " + state.Version) : "Loader not installed";
            _status.Text = state.Conflicts.Count > 0 ? "Conflicts: " + string.Join("; ", state.Conflicts) : message + $" · {state.Mods.Count} mods";
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _busy = false; UpdateState(); }
    }
    private void ShowMods(ModManagerSnapshot state)
    {
        _rows.Clear();
        _mods.Children.Clear();
        foreach (var mod in state.Mods)
        {
            var enabled = new CheckBox { Name = "ModEnabled", Content = new TextBlock { Text = mod.Name, TextWrapping = TextWrapping.Wrap }, IsChecked = mod.Enabled };
            enabled.PropertyChanged += (_, args) => { if (args.Property.Name == nameof(CheckBox.IsChecked)) { UpdateState(); } };
            var row = new StackPanel { Spacing = 4, Children = { enabled, new TextBlock { Text = mod.Id + " · " + mod.Kind, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = mod.File, TextWrapping = TextWrapping.Wrap } } };
            if (mod.Conflicts.Count > 0)
            {
                row.Children.Add(new TextBlock { Text = "Conflicts: " + string.Join("; ", mod.Conflicts), Foreground = Brush.Parse("#F0A070"), TextWrapping = TextWrapping.Wrap });
            }

            var remove = new Button { Name = "RemoveMod", Content = "Remove entry · keep files" };
            ToolTip.SetTip(remove, mod.Id);
            remove.Click += async (_, _) => await Run(root => _client.Remove(root, mod.Id), "Mod entry removed; source files retained");
            row.Children.Add(remove);
            _rows.Add(new ModRow(mod, enabled, remove));
            _mods.Children.Add(new Border { Child = row, Padding = new Thickness(10), BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#404040"), CornerRadius = new CornerRadius(4) });
        }
        if (state.Mods.Count == 0)
        {
            _mods.Children.Add(new TextBlock { Text = "No managed mods. Add a package or Lua mod to begin.", TextWrapping = TextWrapping.Wrap });
        }
    }
    private void UpdateState()
    {
        var current = false;
        try
        {
            current = _loadedDirectory?.Equals(DirectoryPath(), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
        }
        catch (ArgumentException) { }
        _mods.IsVisible = current;
        if (!current && !_busy)
        {
            _installed.Text = "Refresh to inspect this installation";
        }

        var writable = !_busy && current && _snapshot?.Conflicts.Count == 0;
        _refresh.IsEnabled = !_busy;
        _directory.IsEnabled = _browse.IsEnabled = _close.IsEnabled = !_busy;
        _install.IsEnabled = writable;
        _uninstall.IsEnabled = !_busy && current && _snapshot?.Installed == true;
        _addFile.IsEnabled = _addFolder.IsEnabled = writable && _snapshot?.Installed == true;
        foreach (var row in _rows)
        {
            row.Enabled.IsEnabled = !_busy && current && _snapshot?.Installed == true;
        }

        var pending = _rows.Any(row => row.Mod.Enabled != (row.Enabled.IsChecked == true));
        foreach (var row in _rows)
        {
            row.Remove.IsEnabled = !_busy && current && !pending;
        }

        var conflicts = _rows.Any(row => row.Enabled.IsChecked == true && row.Mod.Conflicts.Count > 0);
        _apply.IsEnabled = !_busy && current && _snapshot?.Installed == true && pending && !conflicts;
        _pending.Text = pending ? "Activation changes are pending. Apply validates the selected mods before saving." : "";
    }
}
