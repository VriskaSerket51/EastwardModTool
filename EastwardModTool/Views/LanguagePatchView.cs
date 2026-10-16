using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EastwardLib.Compilation;
using EastwardLib.MetaData;
using EastwardModTool.Core;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class LanguagePatchView : Window
{
    private sealed record SourceRow(AutoCompleteBox Asset, TextBox Source, CheckBox LanguageOnly);
    private readonly GameWorkspace _workspace;
    private readonly TextBox _language = new() { Name = "PatchLanguage", PlaceholderText = "ko" };
    private readonly TextBox _displayName = new() { Name = "PatchDisplayName", PlaceholderText = "한국어" };
    private readonly TextBox _baseLanguage = new() { Name = "PatchBaseLanguage", Text = "en" };
    private readonly TextBox _translations = new() { Name = "PatchTranslations", PlaceholderText = "Translation JSON file (optional)" };
    private readonly TextBox _locales = new() { Name = "PatchLocaleDirectory", PlaceholderText = "Locale PO/source folder (optional)" };
    private readonly AutoCompleteBox _font;
    private readonly TextBox _fontSource = new() { Name = "PatchFontSource", PlaceholderText = "TTF/OTF source file (optional)" };
    private readonly AutoCompleteBox _style;
    private readonly TextBox _originalFonts = new() { Name = "PatchOriginalFonts", AcceptsReturn = true, MinHeight = 65, PlaceholderText = "Optional existing font paths, one per line" };
    private readonly TextBox _lineSpacing = new() { Name = "PatchDialogLineSpacing", PlaceholderText = "Use base-language spacing" };
    private readonly TextBox _buttonSpacing = new() { Name = "PatchFirstRunSpacing", PlaceholderText = "Use default button spacing" };
    private readonly AutoCompleteBox _titleTexture;
    private readonly ComboBox _chapterGroup = new() { Name = "PatchChapterGroup", ItemsSource = new[] { "Base-language default", "EN", "JA", "CN", "CNT", "FR", "DE", "ES" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _dialogClass = new() { Name = "PatchDialogClass", ItemsSource = new[] { "Base-language default", "latin", "cn", "ja" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _initial = new() { Name = "PatchInitialMenu", Content = "Include the initial language selection menu", IsChecked = true };
    private readonly StackPanel _sources = new() { Spacing = 10 };
    private readonly List<SourceRow> _rows = [];
    private readonly TextBlock _status = new() { Name = "LanguagePatchStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 8) };
    private readonly Button _create = new() { Name = "CreateLanguagePatch", Content = "Create editing drafts" };
    private readonly StackPanel _form = new() { Spacing = 8, Margin = new Thickness(18) };
    public Func<LanguagePatchOptions, IReadOnlyList<AssetDocument>> CreatePatch
    {
        get; set;
    }

    public LanguagePatchView(GameWorkspace workspace)
    {
        _workspace = workspace;
        CreatePatch = workspace.CreateLanguagePatch;
        Title = "Create language patch";
        Width = 740;
        Height = 800;
        MinWidth = 440;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _font = AssetInput("PatchFontAsset", "Existing font asset", info => info.Type == "font_ttf");
        _style = AssetInput("PatchStyleSheet", "Existing dialogue stylesheet", info => info.Type == "stylesheet");
        _titleTexture = AssetInput("PatchTitleTexture", "Existing DLC title texture (optional)", info => info.Type == "texture");
        Heading("Language");
        Field("Language ID", _language);
        Field("Display name", _displayName);
        Field("Use this language as the starting point", _baseLanguage);
        Note("The base language fills missing translations. Existing target translations are kept; your JSON or locale source folder supplies the changes.");
        PathField("Translation JSON", _translations, false);
        PathField("Locale source folder", _locales, true);
        Heading("Images and other resource replacements");
        Note("Language-only images create a new texture for this language. Chapter graphics, DLC titles and supported image references use the new image. Keep the original image dimensions to preserve layout. Shared replacements of PSD packs, deck atlases, sprites and other resources affect every language that uses them.");
        _form.Children.Add(_sources);
        var add = new Button { Name = "AddLanguageSource", Content = "Add resource replacement" };
        add.Click += (_, _) => AddSource();
        _form.Children.Add(add);
        Heading("Font and dialogue");
        Field("Font asset to use as a starting point", _font);
        PathField("Font source", _fontSource, false);
        Field("Dialogue stylesheet", _style);
        Field("Other original font references to redirect", _originalFonts);
        Field("Dialogue layout class", _dialogClass);
        Field("Dialogue line spacing (optional)", _lineSpacing);
        Note("A supplied TTF/OTF file creates a language-specific font asset. The stylesheet and listed original font references select where to use it for this language. Leave both blank to redirect all regular text.");
        Heading("Language-specific graphics and menu");
        Field("Chapter graphic group", _chapterGroup);
        Field("DLC title texture", _titleTexture);
        _form.Children.Add(_initial);
        Field("Initial language-menu button spacing (optional)", _buttonSpacing);
        Note("Creates editable asset drafts. Review them, save the project, then use Mod → Export mod or Build archives.");
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 4), Children = { _create, cancel } };
        _create.Margin = new Thickness(0, 0, 8, 0);
        _create.Click += async (_, _) =>
        {
            try
            {
                var options = ReadOptions();
                _create.IsEnabled = _form.IsEnabled = false;
                _status.Text = "Preparing translation, graphics and menu drafts…";
                await Task.Yield();
                var documents = CreatePatch(options);
                Close(documents);
            }
            catch (Exception error) { _status.Text = error.Message; }
            finally { _create.IsEnabled = _form.IsEnabled = true; }
        };
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(_status);
        panel.Children.Add(new ScrollViewer { Name = "LanguagePatchScroll", Content = _form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = panel;
    }
    private AutoCompleteBox AssetInput(string name, string placeholder, Func<AssetInfo, bool> filter)
    {
        return new AutoCompleteBox { Name = name, PlaceholderText = placeholder, ItemsSource = _workspace.Assets.Values.Where(filter).Select(info => info.AssetName).Order(StringComparer.Ordinal).ToArray(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    }
    private void Heading(string text) => _form.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
    private void Note(string text) => _form.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6) });
    private void Field(string label, Control input)
    {
        _form.Children.Add(new TextBlock { Text = label });
        _form.Children.Add(input);
    }
    private void PathField(string label, TextBox input, bool folder)
    {
        var browse = new Button { Content = folder ? "Folder…" : "File…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) => { var path = await PickSource(folder); if (path != null) { input.Text = path; } };
        var panel = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(browse);
        panel.Children.Add(input);
        Field(label, panel);
    }
    private async Task<string?> PickSource(bool folder)
    {
        if (folder)
        {
            return (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select source folder", AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
        }

        return (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select source file", AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    }
    public void AddSource(string? asset = null, string? source = null)
    {
        var input = AssetInput("PatchSourceAsset", "Existing image or resource asset path", info => AssetCompiler.Supports(info.Type));
        input.Text = asset;
        var path = new TextBox { Name = "PatchSourceFile", Text = source, PlaceholderText = "Replacement source file or folder" };
        var local = new CheckBox { Name = "PatchLanguageOnly", Content = "Language-only image (texture asset)", IsChecked = true };
        string? sourceType = null;
        void UpdateScope()
        {
            if (_workspace.Assets.TryGetValue(input.Text ?? "", out var info))
            {
                var texture = info.Type == "texture";
                if (sourceType != info.Type && texture)
                {
                    local.IsChecked = true;
                }

                sourceType = info.Type;
                local.IsEnabled = texture;
                if (!texture)
                {
                    local.IsChecked = false;
                }
            }
            else
            {
                local.IsEnabled = true;
            }
        }
        input.PropertyChanged += (_, args) => { if (args.Property.Name == nameof(AutoCompleteBox.Text)) { UpdateScope(); } };
        UpdateScope();
        var row = new StackPanel { Spacing = 6 };
        var remove = new Button { Content = "Remove" };
        var file = new Button { Content = "File…", Margin = new Thickness(0, 0, 8, 0) };
        var folder = new Button { Content = "Folder…", Margin = new Thickness(0, 0, 8, 0) };
        file.Click += async (_, _) => { var selected = await PickSource(false); if (selected != null) { path.Text = selected; } };
        folder.Click += async (_, _) => { var selected = await PickSource(true); if (selected != null) { path.Text = selected; } };
        row.Children.Add(input);
        row.Children.Add(path);
        row.Children.Add(local);
        row.Children.Add(new WrapPanel { Children = { file, folder, remove } });
        var entry = new SourceRow(input, path, local);
        remove.Click += (_, _) => { _rows.Remove(entry); _sources.Children.Remove(row); };
        _rows.Add(entry);
        _sources.Children.Add(row);
    }
    private static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static double? OptionalNumber(string? text)
    {
        var value = Optional(text);
        if (value == null)
        {
            return null;
        }

        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number <= 0)
        {
            throw new ArgumentException("Spacing must be a positive number.");
        }

        return number;
    }
    public LanguagePatchOptions ReadOptions()
    {
        var language = Optional(_language.Text) ?? throw new ArgumentException("Enter a language ID.");
        var display = Optional(_displayName.Text) ?? throw new ArgumentException("Enter a display name.");
        var baseLanguage = Optional(_baseLanguage.Text) ?? throw new ArgumentException("Enter a base language.");
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _rows)
        {
            var asset = Optional(row.Asset.Text);
            var source = Optional(row.Source.Text);
            if (asset == null && source == null)
            {
                continue;
            }

            if (asset == null || source == null)
            {
                throw new ArgumentException("Each resource replacement needs an asset and a source path.");
            }

            if (sources.ContainsKey(asset) || images.ContainsKey(asset))
            {
                throw new ArgumentException("Choose each replacement asset once: " + asset);
            }

            if (row.LanguageOnly.IsChecked == true)
            {
                if (!_workspace.Assets.TryGetValue(asset, out var info) || info.Type != "texture")
                {
                    throw new ArgumentException("Choose a texture asset for a language-only image.");
                }

                images.Add(asset, source);
            }
            else
            {
                sources.Add(asset, source);
            }
        }
        return new LanguagePatchOptions
        {
            Language = language,
            DisplayName = display,
            BaseLanguage = baseLanguage,
            LocaleDirectory = Optional(_locales.Text),
            TranslationFile = Optional(_translations.Text),
            SourceFiles = sources,
            LocalizedImages = images,
            FontAsset = Optional(_font.Text),
            FontSource = Optional(_fontSource.Text),
            OriginalFontAssets = (_originalFonts.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList(),
            DialogLineSpacing = OptionalNumber(_lineSpacing.Text),
            FirstRunButtonSpacing = OptionalNumber(_buttonSpacing.Text),
            StyleSheetAsset = Optional(_style.Text),
            ChapterGroup = _chapterGroup.SelectedIndex <= 0 ? null : _chapterGroup.SelectedItem?.ToString(),
            TitleTexture = Optional(_titleTexture.Text),
            DialogClass = _dialogClass.SelectedIndex <= 0 ? null : _dialogClass.SelectedItem?.ToString(),
            IncludeInitialMenu = _initial.IsChecked == true
        };
    }
}
