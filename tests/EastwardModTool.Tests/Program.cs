using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EastwardModTool.Editing;
using EastwardLib.Assets;
using EastwardLib.Compilation;
using EastwardLib.MetaData;
using EastwardLib.Modding;
using EastwardLib.Utils;
using EastwardModTool;
using EastwardModTool.Core;
using EastwardModTool.Core.Navigation;
using EastwardModTool.Core.Documents;
using EastwardModTool.Views;

if (args.Length >= 2 && args[0] == "--mod-manager-ui")
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    Directory.CreateDirectory(args[1]);
    ModManagerUI(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--language-patch-ui")
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    Directory.CreateDirectory(args[1]);
    LanguagePatchUI(args[1]);
    return;
}

if (args.Length >= 3 && args[0] == "--language-images")
{
    LanguageImages(args[1], args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "--views")
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    Directory.CreateDirectory(args[2]);
    using var workspace = GameWorkspace.Open(args[1]);
    var editor = new AssetEditorView();
    var window = new Window { Content = editor, Width = 1100, Height = 750 };
    window.Show();
    var failures = new List<string>();
    foreach (var group in workspace.Assets.Values.Where(info => info.Type != "folder").GroupBy(info => info.Type).OrderBy(group => group.Key))
    {
        var info = group.First();
        try
        {
            var document = workspace.OpenAsset(info.AssetName);
            editor.ShowDocument(document);
            for (var frame = 0; frame < 3; frame++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var capture = window.CaptureRenderedFrame();
            }
            using var image = window.CaptureRenderedFrame();
            image!.Save(Path.Combine(args[2], group.Key.Replace('.', '-') + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            if (document is TextDocument)
            {
                editor.GetVisualDescendants().OfType<TextPresentationView>().FirstOrDefault()?.ShowSource();
                for (var frame = 0; frame < 3; frame++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    using var capture = window.CaptureRenderedFrame();
                }
                using var sourceImage = window.CaptureRenderedFrame();
                sourceImage!.Save(Path.Combine(args[2], group.Key.Replace('.', '-') + "-source.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            Check(!document.IsDirty, "Preview marked an asset dirty");
            Console.WriteLine($"VIEW {group.Key}: {(document is UnavailableDocument ? "unavailable notice" : document.Baseline.GetType().Name)}");
        }
        catch (Exception error) { failures.Add(group.Key + ": " + error.Message); Console.WriteLine("FAIL VIEW " + failures[^1]); }
    }
    editor.Dispose();
    window.Close();
    Check(failures.Count == 0, string.Join("\n", failures));
    Console.WriteLine("PASS every installed asset type rendered without changing data; game was not started");
    return;
}

if (args.Length >= 2 && args[0] == "--catalog")
{
    using var workspace = GameWorkspace.Open(args[1]);
    foreach (var group in workspace.Assets.Values.Where(info => info.Type != "folder").GroupBy(info => info.Type).OrderBy(group => group.Key))
    {
        var info = group.First();
        try
        {
            var document = workspace.OpenAsset(info.AssetName);
            var example = document is TextDocument text ? text.Text : document.Baseline is JsonAsset json ? json.ToJson() : document.Baseline is SceneAsset scene ? new JsonAsset(scene.Index).ToJson() : document.Baseline is PackageAsset pack ? string.Join(",", pack.Items.Keys) : "";
            Console.WriteLine($"{group.Key} ({group.Count()}) {document.Baseline.GetType().Name} {info.AssetName}\n{example[..Math.Min(550, example.Length)]}");
        }
        catch (Exception error) { Console.WriteLine($"{group.Key} ({group.Count()}) unavailable: {error.Message}"); }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--game")
{
    InstalledGame(args[1], args.Length > 2 ? args[2] : null);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "EastwardModTool-tests-" + Guid.NewGuid());
Navigation();
SyntaxHighlighting();
DeckPreview();
BundledLua();
Directory.CreateDirectory(root);
try
{
    DocumentNavigation(Path.Combine(root, "navigation"));
    var unicodeData = new JsonObject { ["한글"] = "안녕 日本語", ["controls"] = "\"\n\t\\", ["literal"] = "\\uAC00" };
    var unicodeAsset = new JsonAsset(unicodeData);
    var sourcePath = Path.Combine(root, "unicode.json");
    unicodeAsset.SaveTo(sourcePath);
    var unicodeSource = File.ReadAllText(sourcePath);
    Check(unicodeSource.Contains("한글") && unicodeSource.Contains("안녕 日本語") && JsonNode.DeepEquals(JsonNode.Parse(unicodeSource), unicodeData), "JSON export changed Unicode or required escapes");
    Check(unicodeSource.Contains("\\\\uAC00") && Encoding.UTF8.GetString(unicodeAsset.Encode()) == unicodeData.ToJsonString(), "Readable JSON output changed literal escape text or runtime serialization");
    var manager = Fixture(root);
    using (var workspace = new GameWorkspace(manager, root))
    {
        var packDocument = (ResourceDocument)workspace.OpenAsset("props.deckpack");
        Check(packDocument.Current is DeckPackAsset originalPack && originalPack.DeckNames.Count == 2 && packDocument.OpenDeck("box2").GetImage()!.Width == 4, "Parent pack did not expose actual deck definitions");
        var packSource = Path.Combine(root, "deck-source");
        packDocument.ExportSource(packSource);
        packDocument.ImportSource(packSource);
        Check(packDocument.Current is DeckPackAsset importedPack && importedPack.DeckNames.SequenceEqual(new[] { "box", "box2" }) && packDocument.OpenDeck("box").GetImage()!.Width == 2, "Source import lost the typed deck pack or its preview");
        packDocument.Reset();
        var lua = workspace.OpenAsset("fixture.lua");
        Check(lua is TextDocument luaText && luaText.CanEdit && !luaText.IsDirty && luaText.Text == "", "Workspace did not automatically decompile bundled Lua bytecode");
        var text = (TextDocument)workspace.OpenAsset("hello.txt");
        text.Text = "edited";
        Check(text.IsDirty && text.CanUndo && workspace.HasChanges, "Text editing did not mark the workspace");
        text.Undo();
        Check(text.Text == "original" && !workspace.HasChanges && text.CanRedo, "Undo did not restore the original document");
        text.Redo();
        text.Text = "another edit";
        text.Undo();
        text.Text = "branch";
        Check(!text.CanRedo, "Editing after undo did not discard the old redo branch");
        var json = (TextDocument)workspace.OpenAsset("settings.json");
        Check(json.Text.Contains("안녕") && !json.IsDirty && ((TextDocument)workspace.OpenAsset("@asset_index")).Text.Contains("한글 메모"), "JSON editor escaped Unicode or marked formatting as an edit");
        json.Text = "invalid json";
        Reject(() => workspace.BuildMod("invalid"));
        Check(Encoding.UTF8.GetString(manager.LoadFile("fixture/text")!) == "original", "Invalid edits changed the original archive");
        json.Reset();
        var sq = (TextDocument)workspace.OpenAsset("event.sq");
        sq.Text = "!start\nsay modified";
        var graph = (TextDocument)workspace.OpenAsset("fsm.graphml");
        graph.Text = graph.Text.Replace("next", "done", StringComparison.Ordinal);
        var import = Path.Combine(root, "import.txt");
        File.WriteAllText(import, "imported");
        text.ImportSource(import);
        Check(text.Text == "imported", "Source import did not refresh text");
        var mod = workspace.BuildMod("Core test");
        Check(Encoding.UTF8.GetString(mod.GetResource("fixture/text")) == "imported" && JsonNode.Parse(mod.GetResource("fixture/sq"))!["children"]![1]!["args"]![0]!.ToString() == "modified", "Workspace compiled incorrect text or SQ");
        var package = Path.Combine(root, "test.ewmod");
        mod.SaveTo(package);
        workspace.ApplyMod(ModPackage.Read(package), Path.Combine(root, "applied"));
        Check(Encoding.UTF8.GetString(GArchive.Read(Path.Combine(root, "applied", "fixture.g"))["fixture/text"]) == "imported", "Workspace mod application failed");
        text.Reset();
        sq.Reset();
        graph.Reset();
        Check(!workspace.HasChanges, "Reverting all documents left dirty state");
        var image = (ResourceDocument)workspace.OpenAsset("texture.png");
        var replacement = Path.Combine(root, "replacement.png");
        new RgbaImage(1, 1, [10, 20, 30, 255]).SavePng(replacement);
        image.ImportSource(replacement);
        Check(image.GetImage()!.Pixels[0] == 10 && image.CanUndo, "Image import did not preserve edit history");
        image.Undo();
        Check(image.GetImage()!.Pixels[0] == 50 && !image.IsDirty, "Image undo changed its baseline");
        var sprite = (ResourceDocument)workspace.OpenAsset("sprite.msprite");
        Check(sprite.GetFrameDuration("idle", 0) == TimeSpan.FromSeconds(0.1) && sprite.GetFrameDuration("idle", 1) == TimeSpan.FromSeconds(0.2), "Sprite preview lost per-frame timings");
        Reject(() => sprite.GetFrameDuration("idle", 2));
        var frame = sprite.GetImage("idle")!;
        var offset = (3 * 5 + 2) * 4;
        Check(frame.Width == 5 && frame.Height == 5 && frame.Pixels[offset] == 85 && frame.Pixels[offset + 1] == 170 && frame.Pixels[offset + 3] == 192, "Sprite preview lost shared bounds, origin or alpha compositing");
        var secondFrame = sprite.GetImage("idle", 1)!;
        Check(secondFrame.Width == frame.Width && secondFrame.Height == frame.Height && secondFrame.Pixels[0] == 255 && secondFrame.Pixels[(4 * 5 + 4) * 4 + 1] == 255 && secondFrame.Pixels[offset + 3] == 0, "Sprite frames drifted from runtime part positions or applied unused sequence offsets");
        Check(((MSpriteAsset)sprite.Current).GetPreviewBounds() == (-2, -3, 5, 5), "Sprite preview origin did not match shared runtime bounds");
        text.Text = "unfinished work";
        json.Text = "invalid draft";
        image.ImportSource(replacement);
        var project = Path.Combine(root, "project.ewproject");
        var mesh = (ResourceDocument)workspace.OpenAsset("mesh.obj");
        var meshSource = Path.Combine(root, "mesh-source");
        mesh.ExportSource(meshSource);
        mesh.ImportSource(meshSource);
        Check(mesh.Current is MeshAsset && mesh.Compile() is MeshAsset, "Mesh import lost its typed source representation");
        WorkspaceProject.Save(workspace, project);
        Check(!workspace.IsModified && workspace.HasChanges, "Saving drafts discarded mod edits or left unsaved project state");
        text.Text = "later edit";
        Check(workspace.IsModified, "Editing a saved project did not mark it modified");
        using (var restored = WorkspaceProject.Open(project))
        {
            Check(((TextDocument)restored.OpenAsset("hello.txt")).Text == "unfinished work" && ((TextDocument)restored.OpenAsset("settings.json")).Text == "invalid draft", "Project did not preserve valid and invalid drafts");
            Check(((ResourceDocument)restored.OpenAsset("texture.png")).GetImage()!.Pixels[0] == 10 && !restored.IsModified, "Project lost image edits or its saved state");
            Check(((MeshAsset)((ResourceDocument)restored.OpenAsset("mesh.obj")).Current).MeshData == ((MeshAsset)mesh.Current).MeshData, "Project could not export and recompile an edited mesh");
            Reject(() => restored.BuildMod("invalid draft"));
            ((TextDocument)restored.OpenAsset("settings.json")).Reset();
            Check(restored.BuildMod("restored").GetResource("fixture/text").SequenceEqual("unfinished work"u8.ToArray()), "Restored project could not compile");
        }
        WorkspaceProject.Save(workspace, project);
        var originalBytes = (byte[])manager.LoadFile("fixture/text")!.Clone();
        manager.LoadArchive(GArchive.Read(Path.Combine(root, "fixture.g")));
        var changedArchive = GArchive.Read(Path.Combine(root, "fixture.g"));
        changedArchive["fixture/text"] = "different base"u8.ToArray();
        changedArchive.Write(Path.Combine(root, "fixture.g"));
        Reject(() => WorkspaceProject.Open(project));
        changedArchive["fixture/text"] = originalBytes;
        changedArchive.Write(Path.Combine(root, "fixture.g"));
        json.Reset();
        var metadata = (MetadataDocument)workspace.OpenAsset("@asset_index");
        var metadataJson = JsonNode.Parse(metadata.Text)!;
        metadataJson["hello.txt"]!["unknownModField"] = new JsonObject { ["enabled"] = false };
        metadata.Text = metadataJson.ToJsonString();
        var beforeConfig = manager.LoadFile("config/asset_index")!.ToArray();
        var metadataMod = workspace.BuildMod("metadata edit");
        Check(JsonNode.Parse(metadataMod.GetResource("config/asset_index"))!["hello.txt"]!["unknownModField"]!["enabled"]!.GetValue<bool>() == false && manager.LoadFile("config/asset_index")!.SequenceEqual(beforeConfig) && manager.Index["hello.txt"].Data["unknownModField"] == null, "Metadata editing changed the original index or archive");
        WorkspaceProject.Save(workspace, project);
        using (var restored = WorkspaceProject.Open(project))
        {
            Check(((MetadataDocument)restored.OpenAsset("@asset_index")).Text == metadata.Text && restored.BuildMod("restored metadata").ResourcePaths.Contains("config/asset_index"), "Metadata draft did not restore or compile");
        }
        metadataJson["hello.txt"]!["objectFiles"]!["data"] = "fixture/missing";
        metadata.Text = metadataJson.ToJsonString();
        Reject(() => workspace.BuildMod("missing metadata resource"));
        metadata.Reset();
        var streamOriginal = new TextAsset("old");
        using var stream = new MemoryStream("stream edit"u8.ToArray());
        var streamed = streamOriginal.CompileSource(stream);
        Check(stream.CanRead && Encoding.UTF8.GetString(streamed.Encode()) == "stream edit" && streamOriginal.Text == "old", "Stream compilation owns its input or changes its original");
        var workbook = new WorkbookAsset(JsonNode.Parse("{\"table\":[{\"name\":\"Sam\",\"hp\":10}]}")!, JsonNode.Parse("{\"table\":{\"name\":\"table\",\"type\":\"list\",\"keys\":[\"name\",\"hp\"],\"keyMeta\":[\"localizable\",false],\"unknown\":false}}")!);
        foreach (var extension in new[] { ".xls", ".xlsx" })
        {
            var file = Path.Combine(root, "workbook" + extension);
            workbook.SaveTo(file);
            using var workbookStream = File.OpenRead(file);
            var recompiled = (WorkbookAsset)workbook.CompileSource(workbookStream);
            Check(workbookStream.CanRead && JsonNode.DeepEquals(recompiled.Data, workbook.Data) && JsonNode.DeepEquals(recompiled.Metadata, workbook.Metadata), "Workbook stream import " + extension + " open=" + workbookStream.CanRead + " data=" + recompiled.Data!.ToJsonString() + " meta=" + recompiled.Metadata.ToJsonString());
        }
        text.Reset();
        var largeDraft = new string('x', 2 * 1024 * 1024);
        for (var edit = 0; edit < 20; edit++)
        {
            text.Text = largeDraft + edit;
        }

        var undoSteps = 0;
        while (text.CanUndo)
        {
            text.Undo();
            undoSteps++;
        }
        Check(undoSteps < 20 && text.Text == "original", "Large draft history did not stay bounded or lost its baseline");
        var dictionaryBook = new WorkbookAsset(JsonNode.Parse("{\"dict\":{\"original\":1,\"remove\":2}}")!, JsonNode.Parse("{\"dict\":{\"name\":\"dict\",\"type\":\"dict\",\"sequence\":[\"original\",\"remove\"],\"comment\":{\"original\":\"keep this\"},\"unknown\":false}}")!);
        var editedBook = dictionaryBook.CompileData(JsonNode.Parse("{\"dict\":{\"original\":3,\"added\":4}}")!);
        Check(editedBook.GetRows("dict").Count == 2 && editedBook.GetRows("dict")[1][0]!.ToString() == "added" && editedBook.Metadata["dict"]!["unknown"]!.GetValue<bool>() == false && dictionaryBook.GetRows("dict")[1][0]!.ToString() == "remove", "Workbook data edits lost new keys or changed original metadata");
        Reject(() => workbook.CompileData(JsonNode.Parse("{\"table\":[{\"unexpectedColumn\":1}]}")!));
    }
    Console.WriteLine("PASS document edits, undo/redo, project drafts/baselines, metadata preservation, mesh reimport, mod application, sprite pixels and stream ownership");

    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    StyleViewer(root);
    EngineObjects(root);
    var window = new MainWindow(["--game", root]);
    window.Show();
    Wait(window, () => window.GetVisualDescendants().OfType<AssetBrowserView>().Single().Browser.MatchingCount > 0);
    var list = window.GetVisualDescendants().OfType<AssetBrowserView>().Single();
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text == "original"));
    var editor = window.GetVisualDescendants().OfType<TextEditor>().Single(box => box.Text == "original");
    Check(window.GetVisualDescendants().OfType<TextBox>().Any(box => box.IsReadOnly && box.Text?.Contains("한글 메모") == true), "Metadata inspector escaped Unicode");
    editor.Text = "UI edit";
    editor.CaretOffset = 3;
    var viewMode = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AssetViewMode");
    Check(list.Browser.Mode == AssetBrowseMode.Directories, "Asset browser did not default to Gii directories");
    viewMode.SelectedIndex = 1;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Types);
    Check(list.SelectedAsset!.AssetName == "hello.txt" && ReferenceEquals(editor, window.GetVisualDescendants().OfType<TextEditor>().Single()) && editor.CaretOffset == 3, "Switching asset view lost selection or rebuilt the editor");
    viewMode.SelectedIndex = 0;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Directories);
    list.SelectAsset("actors/enemy/readme.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text == "enemy"));
    var enemyEditor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    enemyEditor.Text = "enemy draft";
    viewMode.SelectedIndex = 1;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Types);
    Check(list.SelectedAsset!.AssetName == "actors/enemy/readme.txt" && ReferenceEquals(enemyEditor, window.GetVisualDescendants().OfType<TextEditor>().Single()) && enemyEditor.Text == "enemy draft", "Same-named asset view switch selected the wrong asset or lost its draft");
    var browserSearch = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "AssetSearch");
    browserSearch.Text = "hello.txt";
    Wait(window, () => list.Browser.MatchingCount == 1 && list.Browser.SelectedNode == null);
    viewMode.SelectedIndex = 0;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Directories);
    Check(list.SelectedAsset!.AssetName == "actors/enemy/readme.txt" && enemyEditor.Text == "enemy draft", "Filtering and view switching discarded a hidden selection");
    browserSearch.Text = "";
    Wait(window, () => list.Browser.SelectedNode != null);
    Check(((AssetTreeNode)window.GetVisualDescendants().OfType<TreeView>().Single().SelectedItem!).Asset!.AssetName == "actors/enemy/readme.txt", "Clearing search did not restore the selected tree node");
    enemyEditor.Text = "enemy";
    var typeFilter = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AssetTypeFilter");
    typeFilter.SelectedItem = "text";
    Wait(window, () => list.Browser.MatchingCount == 3);
    viewMode.SelectedIndex = 1;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Types);
    Check(typeFilter.SelectedItem!.ToString() == "text" && list.Browser.MatchingCount == 3, "View switch reset the shared asset type filter");
    typeFilter.SelectedIndex = 0;
    viewMode.SelectedIndex = 0;
    Wait(window, () => list.Browser.Mode == AssetBrowseMode.Directories && list.Browser.MatchingCount > 3);
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text == "UI edit"));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    Console.WriteLine("PASS directory/type browser defaults, same-named selection, shared filters, hidden selection restoration and preserved editor/caret/drafts");
    editor.Select(1, 3);
    editor.CaretOffset = 4;
    list.SelectAsset("settings.json");
    Wait(window, () => SourceReady(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text.Contains("unknown"))));
    Click("NavigateBack");
    Wait(window, () => list.SelectedAsset!.AssetName == "hello.txt" && window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text == "UI edit"));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    Check(editor.CaretOffset == 4 && editor.SelectionStart == 1 && editor.SelectionLength == 3, "Back navigation lost cursor or selection");
    Click("NavigateForward");
    Wait(window, () => list.SelectedAsset!.AssetName == "settings.json");
    Click("PinDocument");
    var pins = MenuItems(window).Single(item => Equals(item.Header, "Pinned assets"));
    Check(pins.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "settings.json")), "Pin button did not add the full asset name");
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.W, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => !window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "DocumentTab" && Equals(ToolTip.GetTip(button), "settings.json")));
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.T, KeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift });
    Wait(window, () => list.SelectedAsset!.AssetName == "settings.json");
    var helloTab = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "DocumentTab" && Equals(ToolTip.GetTip(button), "hello.txt"));
    helloTab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text == "UI edit"));
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.W, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => !window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "DocumentTab" && Equals(ToolTip.GetTip(button), "hello.txt")));
    Check(window.Title!.Contains("Unsaved"), "Closing a dirty tab discarded the workspace draft");
    Click("ReopenDocument");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text == "UI edit"));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    Check(editor.CaretOffset == 4 && editor.SelectionStart == 1 && editor.SelectionLength == 3, "Reopening a tab lost its editor position");
    Check(window.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "DocumentTab").Count(button => Equals(ToolTip.GetTip(button), "hello.txt")) == 1, "Reopening created duplicate tabs");
    pins.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "settings.json")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Wait(window, () => list.SelectedAsset!.AssetName == "settings.json");
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Left, KeyModifiers = Avalonia.Input.KeyModifiers.Alt });
    Wait(window, () => list.SelectedAsset!.AssetName == "hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    editor.Text = string.Join("\n", Enumerable.Repeat(new string('x', 300), 200));
    using (var capture = window.CaptureRenderedFrame())
    {
    }
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(100, 300);
    Wait(window, () => editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y > 0);
    var savedOffset = editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset;
    list.SelectAsset("settings.json");
    Wait(window, () => SourceReady(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text.Contains("unknown")) && window.GetVisualDescendants().OfType<Menu>().Single(panel => panel.Name == "MainMenu").IsEffectivelyEnabled));
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text.Length > 1000) && window.GetVisualDescendants().OfType<Menu>().Single(panel => panel.Name == "MainMenu").IsEffectivelyEnabled);
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    Wait(window, () => Math.Abs(editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y - savedOffset.Y) < 1 && Math.Abs(editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.X - savedOffset.X) < 1);
    ClickUndo();
    Wait(window, () => editor.Text == "UI edit");
    Console.WriteLine("PASS document tabs, dirty-close/reopen, pinned navigation, history shortcuts and editor position/scroll preservation");

    void ClickUndo()
    {
        var button = MenuItems(window).Single(item => Equals(item.Header, "Undo"));
        Wait(window, () => button.IsEffectivelyEnabled);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    }

    void Click(string name)
    {
        MenuItems(window).Single(item => item.Name == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    }

    using (var capture = window.CaptureRenderedFrame())
    {
        Check(window.Title!.Contains("Unsaved"), "UI editing did not reach document state");
        var screenshot = args.FirstOrDefault() ?? Path.Combine(root, "window.png");
        capture!.Save(screenshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine("RENDER " + screenshot);
    }
    Check(window.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Select(item => item.Header!.ToString()).SequenceEqual(new[] { "_File", "_Edit", "_Asset", "_Mod", "_View" }), "Main menu categories are missing");
    Check(MenuItems(window).Single(item => Equals(item.Header, "Undo")).IsEnabled && MenuItems(window).Single(item => Equals(item.Header, "Redo")).IsEnabled, "Edit menu availability ignored the active document");
    list.SelectAsset("settings.json");
    Wait(window, () => window.GetVisualDescendants().OfType<TextPresentationView>().Any() && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    var presentation = window.GetVisualDescendants().OfType<TextPresentationView>().Single();
    presentation.SelectedIndex = 0;
    Wait(window, () => window.GetVisualDescendants().OfType<TreeView>().Any(tree => tree.Name == "DataTree"));
    var dataTree = window.GetVisualDescendants().OfType<TreeView>().Single(tree => tree.Name == "DataTree");
    var dataRoot = (JsonDataView.Node)dataTree.Items[0]!;
    dataTree.SelectedItem = dataRoot.Children.Single(node => node.Path.Last() == "message");
    var valueEditor = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PropertyValue");
    valueEditor.Text = "\"수정 日本語\"";
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplyProperty").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(window.Title!.Contains("Unsaved"), "Property editing did not reach document history");
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.F, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text.Contains("수정 日本語")));
    var propertySource = window.GetVisualDescendants().OfType<TextEditor>().Single();
    propertySource.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Z, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => propertySource.Text.Contains("안녕"));
    list.SelectAsset("fsm.graphml");
    Wait(window, () => window.GetVisualDescendants().OfType<Canvas>().Any(canvas => canvas.Name == "GraphCanvas") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    Check(window.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "GraphCanvas").Children.Count > 0, "Graph preview has no nodes");
    list.SelectAsset("mesh.obj");
    Wait(window, () => window.GetVisualDescendants().OfType<Canvas>().Any(canvas => canvas.Name == "MeshCanvas") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    Check(window.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "MeshCanvas").Children.Count == 3, "Mesh wireframe lost its stored triangle");
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text == "UI edit") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    Console.WriteLine("PASS property edit/source synchronization and undo, graph default, and mesh wireframe");
    var adaptive = window.GetVisualDescendants().OfType<AdaptiveWorkspaceView>().Single();
    foreach (var (width, layout) in new[] { (1920, WorkspaceLayout.Wide), (1400, WorkspaceLayout.Wide), (1280, WorkspaceLayout.Wide), (1120, WorkspaceLayout.Wide), (1024, WorkspaceLayout.Split), (980, WorkspaceLayout.Split), (800, WorkspaceLayout.Split), (640, WorkspaceLayout.Tabbed), (560, WorkspaceLayout.Tabbed) })
    {
        window.Width = width;
        window.Height = width == 560 ? 480 : 720;
        Wait(window, () => adaptive.Layout == layout && Math.Abs(window.Bounds.Width - width) < 1);
        using var capture = window.CaptureRenderedFrame();
        Check(window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.Text == "UI edit") && window.Title!.Contains("Unsaved"), "Resizing lost the active editor or draft");
        foreach (var button in window.GetVisualDescendants().OfType<WrapPanel>().SelectMany(panel => panel.Children.OfType<Button>()))
        {
            var position = button.TranslatePoint(new Point(), window)!.Value;
            Check(position.X >= 0 && position.X + button.Bounds.Width <= window.Bounds.Width + 1 && position.Y >= 0 && position.Y + button.Bounds.Height <= window.Bounds.Height + 1, $"Responsive layout clipped {button.Content} at {width}: {position}, {button.Bounds}, window {window.Bounds}");
            var panel = button.GetVisualAncestors().OfType<WrapPanel>().First();
            var local = button.TranslatePoint(new Point(), panel)!.Value;
            Check(local.X >= 0 && local.X + button.Bounds.Width <= panel.Bounds.Width + 1 && local.Y >= 0 && local.Y + button.Bounds.Height <= panel.Bounds.Height + 1, $"Responsive panel clipped {button.Content} at {width}: {local}, {button.Bounds}, panel {panel.Bounds}");
        }
        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        foreach (var category in menu.GetVisualDescendants().OfType<MenuItem>())
        {
            var position = category.TranslatePoint(new Point(), window)!.Value;
            Check(position.X >= 0 && position.X + category.Bounds.Width <= window.Bounds.Width + 1, "Responsive menu clipped a category");
        }
        if (args.Length > 0)
        {
            capture!.Save(Path.ChangeExtension(args[0], $"{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.L, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex == 0, "Asset navigation shortcut did not reveal the compact asset panel");
    }

    var compactList = window.GetVisualDescendants().OfType<AssetBrowserView>().Single();
    compactList.SelectAsset("settings.json");
    Wait(window, () => SourceReady(window, () => adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex == 1 && window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text.Contains("unknown"))));
    window.Width = 1400;
    window.Height = 860;
    Wait(window, () => adaptive.Layout == WorkspaceLayout.Wide);
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text == "UI edit"));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
    window.Width = 980;
    Wait(window, () => adaptive.Layout == WorkspaceLayout.Split);
    adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
    window.Width = 640;
    Wait(window, () => adaptive.Layout == WorkspaceLayout.Tabbed);
    Check(adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex == 2, "Resizing lost the selected metadata panel");
    adaptive.ShowEditor();
    window.Width = 1400;
    Wait(window, () => adaptive.Layout == WorkspaceLayout.Wide);
    window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.H, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => window.GetVisualDescendants().OfType<AvaloniaEdit.Search.SearchPanel>().Any(panel => !panel.IsClosed));
    var find = window.GetVisualDescendants().OfType<AvaloniaEdit.Search.SearchPanel>().Single(panel => !panel.IsClosed);
    find.SearchPattern = "edit";
    find.ReplacePattern = "change";
    find.ReplaceAll();
    Wait(window, () => editor.Text == "UI change");
    find.Close();
    editor.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Z, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => editor.Text == "UI edit");
    Console.WriteLine("PASS text search/replace and shared undo history");
    Console.WriteLine("PASS responsive wide/split/tabbed layouts, toolbar containment, compact asset navigation and preserved drafts");
    var undo = MenuItems(window).Single(item => Equals(item.Header, "Undo"));
    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Wait(window, () => editor.Text == "original");
    editor.Text = "keyboard edit";
    editor.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Z, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => editor.Text == "original");
    editor.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Y, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => editor.Text == "keyboard edit");
    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Wait(window, () => editor.Text == "original");
    var search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search assets…");
    search.Text = "texture.png";
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(list.Browser.MatchingCount == 1, "UI asset search failed");
    }

    var indexButton = MenuItems(window).Single(item => item.Name == "EditAssetIndex");
    indexButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Wait(window, () => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "asset_index"));
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => !box.IsReadOnly && box.Text.Contains("objectFiles")));
    var metadataEditor = window.GetVisualDescendants().OfType<TextEditor>().Single(box => !box.IsReadOnly && box.Text?.Contains("objectFiles") == true);
    metadataEditor.Text += " ";
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(window.Title!.Contains("Unsaved"), "Metadata UI did not edit its draft");
    }

    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Wait(window, () => !window.Title!.Contains("Unsaved"));
    search.Text = "sprite.msprite";
    Wait(window, () => list.Browser.MatchingCount > 0);
    list.SelectAsset("sprite.msprite");
    Wait(window, () => window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null));
    var frameSlider = window.GetVisualDescendants().OfType<Slider>().Single();
    frameSlider.Value = 1;
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    list.SelectAsset("sprite.msprite");
    Wait(window, () => window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null));
    Check(window.GetVisualDescendants().OfType<Slider>().Single().Value == 1, "Switching tabs lost the selected sprite frame");
    using (var capture = window.CaptureRenderedFrame())
    {
    }
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var spriteView = window.GetVisualDescendants().OfType<AssetEditorView>().Single();
    frameSlider = window.GetVisualDescendants().OfType<Slider>().Single();
    var loop = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "LoopAnimation");
    loop.IsChecked = false;
    var play = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PlayAnimation");
    play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(spriteView.IsPlaying && frameSlider.Value == 0 && Equals(play.Content, "Pause"), "Play did not restart a completed animation");
    Wait(window, () => frameSlider.Value == 1);
    Wait(window, () => !spriteView.IsPlaying);
    Check(frameSlider.Value == 1 && Equals(play.Content, "Play"), "Non-looping animation did not stop on its last frame");
    loop.IsChecked = true;
    play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => frameSlider.Value == 0);
    Check(spriteView.IsPlaying, "Looping animation stopped at the end");
    play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(!spriteView.IsPlaying, "Pause did not stop playback");
    play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    frameSlider.Value = frameSlider.Value == 0 ? 1 : 0;
    Check(!spriteView.IsPlaying, "Manual frame scrubbing did not pause playback");
    play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any());
    Check(!spriteView.IsPlaying, "Leaving a sprite tab kept its timer running");
    Console.WriteLine("PASS original animation timings, play/pause, loop, final-frame stop, scrub pause and tab cleanup");
    Wait(window, () => window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    list.SelectAsset("props.deckpack/box");
    Wait(window, () => window.GetVisualDescendants().OfType<TabControl>().Any(tabs => tabs.Name == "DeckViews") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    var deckViews = window.GetVisualDescendants().OfType<TabControl>().Single(tabs => tabs.Name == "DeckViews");
    var deckImage = window.GetVisualDescendants().OfType<Image>().Single(image => image.Source != null);
    var firstDeckImage = deckImage.Source;
    var channels = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "DeckChannel");
    channels.SelectedItem = "Normal";
    Check(deckImage.Source != firstDeckImage && !MenuItems(window).Single(item => Equals(item.Header, "Import file…")).IsEnabled, "Deck channel did not update its preview or virtual editing was enabled");
    if (args.Length > 0)
    {
        using (var capture = window.CaptureRenderedFrame())
        {
            capture!.Save(Path.ChangeExtension(args[0], "deck.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    deckViews.SelectedIndex = 1;
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => editor.IsReadOnly && editor.Text.Contains("meshes")));
    list.SelectAsset("sprite.msprite/idle");
    Wait(window, () => window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "PlayAnimation" && button.IsVisible) && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    Check(window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null), "Sprite sequence deck did not display an image");
    list.SelectAsset("props.deckpack/box");
    Wait(window, () => window.GetVisualDescendants().OfType<ComboBox>().Any(box => box.Name == "DeckChannel" && Equals(box.SelectedItem, "Normal")) && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    list.SelectAsset("props.deckpack");
    Wait(window, () => window.GetVisualDescendants().OfType<DeckPackEditorView>().Any() && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    var packView = window.GetVisualDescendants().OfType<DeckPackEditorView>().Single();
    var packList = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "PackDeckList");
    Check(packList.Items.Cast<string>().SequenceEqual(new[] { "box", "box2" }) && packView.SelectedName == "box", "Parent PSD did not list its decks or select the first preview");
    packList.SelectedItem = "box2";
    Wait(window, () => window.GetVisualDescendants().OfType<Image>().Any(image => image.Source is Avalonia.Media.Imaging.Bitmap bitmap && bitmap.PixelSize == new PixelSize(4, 3)));
    var packSearch = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PackDeckSearch");
    packSearch.Text = "box2";
    Wait(window, () => packList.Items.Count == 1);
    Check(packList.Items.Count == 1 && packView.SelectedName == "box2", "Searching parent decks lost the selected preview");
    packSearch.Text = "box";
    Wait(window, () => packList.Items.Count == 2);
    Check(packList.Items.Count == 2 && Equals(packList.SelectedItem, "box2"), "Restoring the parent list lost selection");
    if (args.Length > 0)
    {
        using (var capture = window.CaptureRenderedFrame())
        {
            capture!.Save(Path.ChangeExtension(args[0], "parent.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    list.SelectAsset("hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(editor => !editor.IsReadOnly) && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    list.SelectAsset("props.deckpack");
    Wait(window, () => window.GetVisualDescendants().OfType<DeckPackEditorView>().Any(view => view.SelectedName == "box2") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
    Console.WriteLine("PASS parent PSD deck list, search, selection, preview, source round trip and child deck views");
    window.Width = 640;
    Wait(window, () => adaptive.Layout == WorkspaceLayout.Tabbed);
    while (window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "DocumentTab"))
    {
        window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.W, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    }
    Check(adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex == 0, "Closing the last document did not return to Assets");
    Click("ReopenDocument");
    Wait(window, () => adaptive.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex == 1);
    Console.WriteLine("PASS closing all documents returns to Assets and reopening returns to Editor");
    window.Close();
    Console.WriteLine("PASS headless desktop layout, live text editing, undo and search");
}
finally
{
    Directory.Delete(root, true);
}

static void EngineObjects(string directory)
{
    var scalarGraph = new SerializedObjectGraph(JsonNode.Parse("""{"map":{"layer":{"model":false,"body":"main"},"alias":{"alias":"layer"}}}"""));
    Check(scalarGraph.Label("layer") == "main · value" && scalarGraph.Label("alias").Contains("layer"), "Serializer primitive objects or aliases lost their meaning");
    var manager = Fixture(directory);
    using var workspace = new GameWorkspace(manager, directory);
    var scene = (ResourceDocument)workspace.OpenAsset("objects.scene");
    var baseline = scene.SceneData["default.scene_group"]!.DeepClone();
    var editor = new AssetEditorView();
    editor.ShowDocument(scene);
    var window = new Window { Content = editor, Width = 900, Height = 750 };
    window.Show();
    Wait(window, () => window.GetVisualDescendants().OfType<TreeView>().Any(tree => tree.Name == "SceneHierarchy" && tree.Items.Count > 0));
    var hierarchy = window.GetVisualDescendants().OfType<TreeView>().Single(tree => tree.Name == "SceneHierarchy");
    var hero = (SceneHierarchyView.Entity)hierarchy.Items[0]!;
    Check(hero.Label.Contains("Hero") && hero.Children.Any(child => child.Label.Contains("(reference)")), "Scene names or cyclic hierarchy reference protection failed");
    hierarchy.SelectedItem = hero.Children.Single(child => child.Id == "component");
    Check(window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ObjectLocation").Text!.Contains("component"), "Scene component selection lost object ID");
    var tree = window.GetVisualDescendants().OfType<TreeView>().Single(tree => tree.Name == "DataTree");
    tree.SelectedItem = ((JsonDataView.Node)tree.Items[0]!).Children.Single(node => node.Path.Last() == "material");
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenObjectReference").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ObjectLocation").Text!.Contains("PhysicsMaterial"), "Scene component inspector lost group-wide reference map");
    tree.SelectedItem = ((JsonDataView.Node)tree.Items[0]!).Children.Single(node => node.Path.Last() == "friction");
    window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PropertyValue").Text = "0.8";
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplyProperty").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "ObjectLocation" && block.Text!.Contains("PhysicsMaterial")));
    Check(scene.IsDirty && scene.SceneData["default.scene_group"]!["map"]!["material"]!["body"]!["friction"]!.GetValue<double>() == 0.8 && baseline["map"]!["material"]!["body"]!["friction"]!.GetValue<double>() == 0.5, "Scene property edit did not update parent or mutated baseline");
    Check(JsonNode.DeepEquals(((SceneAsset)scene.Baseline).Groups["default.scene_group"], baseline), "Scene editor changed original asset data");
    var resources = new AssetCompiler(manager).Compile(scene.Name, scene.Compile());
    Check(resources.ContainsKey("fixture/scene/default.scene_group") && resources.ContainsKey("fixture/scene-bin/default.scene_group.packed") && resources["fixture/scene/preserved.bin"].SequenceEqual(new byte[] { 1, 2, 3 }), "Scene group edit did not compile source, packed resource and preserved package data");
    scene.Undo();
    Check(!scene.IsDirty && JsonNode.DeepEquals(scene.SceneData["default.scene_group"], baseline), "Scene parent undo did not restore group");
    var view = window.GetVisualDescendants().OfType<SceneHierarchyView>().Single();
    view.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(text => text.Name == "SceneGroupSource"));
    var source = window.GetVisualDescendants().OfType<TextEditor>().Single(text => text.Name == "SceneGroupSource");
    source.Text = "{";
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplySceneSource").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(!scene.IsDirty, "Invalid scene group Source changed the package");
    source.Text = new JsonAsset(baseline).ToJson().Replace("Hero", "Changed hero");
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplySceneSource").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(scene.IsDirty && scene.SceneData["default.scene_group"]!["map"]!["hero"]!["body"]!["name"]!.ToString() == "Changed hero", "Scene group Source did not reach parent document");
    scene.Undo();
    window.Width = 450;
    Wait(window, () => view.Bounds.Width < 620);
    Check(view.GetVisualDescendants().OfType<Grid>().Any(grid => grid.RowDefinitions.Count == 2), "Narrow scene viewer did not stack hierarchy and inspector");
    var prefab = (TextDocument)workspace.OpenAsset("objects.prefab");
    editor.ShowDocument(prefab);
    Wait(window, () => editor.GetVisualDescendants().OfType<SceneHierarchyView>().Any());
    prefab.Text = "{";
    Wait(window, () => editor.GetVisualDescendants().OfType<TreeView>().Single(item => item.Name == "SceneHierarchy").Items.Count == 0);
    prefab.Undo();
    Wait(window, () => editor.GetVisualDescendants().OfType<TreeView>().Single(item => item.Name == "SceneHierarchy").Items.Count > 0);
    editor.ShowDocument(workspace.OpenAsset("objects.material"));
    Wait(window, () => editor.GetVisualDescendants().OfType<ComboBox>().Any(combo => combo.Name == "EngineObjects" && combo.Items.Count == 3));
    Check(!prefab.IsDirty, "Prefab invalid Source recovery lost original data");
    editor.Dispose();
    window.Close();
    Console.WriteLine("PASS scene hierarchy cycles, component references, parent edits/undo, packed compilation, preserved files, group Source, narrow layout, prefab recovery and model objects");
}

static void StyleViewer(string directory)
{
    using var workspace = GameWorkspace.Open(directory);
    var document = (TextDocument)workspace.OpenAsset("text.stylesheet");
    var original = JsonNode.Parse(document.Text);
    var presentation = new AssetEditorView();
    presentation.ShowDocument(document);
    var window = new Window { Content = presentation, Width = 900, Height = 750 };
    window.Show();
    Wait(window, () => window.GetVisualDescendants().OfType<ListBox>().Any(list => list.Name == "StyleList" && list.Items.Count == 2));
    var list = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "StyleList");
    Check(((StyleSheetView.Style)list.SelectedItem!).Id == "!10002" && !document.IsDirty, "Stylesheet did not preserve ordered references or marked viewing dirty");
    var apply = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplyStyle");
    apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(!document.IsDirty, "Applying unchanged style changed original data");
    var red = window.GetVisualDescendants().OfType<NumericUpDown>().Single(box => box.Name == "StyleColor0");
    red.Value = 0.2m;
    Check(((Avalonia.Media.SolidColorBrush)window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "StyleSwatch").Background!).Color.R == 51 && !document.IsDirty, "RGBA preview did not update independently of document Apply");
    red.Value = 1;
    var size = window.GetVisualDescendants().OfType<NumericUpDown>().Single(box => box.Name == "StyleSize");
    size.Value = 1.5m;
    apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(!document.IsDirty, "Stylesheet accepted a fractional engine integer size");
    size.Value = 16;
    window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "StyleName").Text = "제목 日本語";
    window.GetVisualDescendants().OfType<NumericUpDown>().Single(box => box.Name == "StyleSize").Value = 32;
    apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    var edited = JsonNode.Parse(document.Text)!;
    Check(edited["map"]!["!10002"]!["body"]!["name"]!.GetValue<string>() == "제목 日本語" && edited["map"]!["!10002"]!["body"]!["size"]!.GetValue<decimal>() == 32, "Stylesheet edits did not reach Source");
    Check(edited["root"]!.ToString() == "!10001" && JsonNode.DeepEquals(edited["map"]!["!10001"], original!["map"]!["!10001"]) && edited["map"]!["!10002"]!["body"]!["unknown"]!["keep"]!.GetValue<bool>() == false && edited["unknown"]!.GetValue<bool>() == false, "Stylesheet edits lost references or unknown fields");
    document.Undo();
    Wait(window, () => window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "StyleName").Text == "default");
    Check(!document.IsDirty && JsonNode.DeepEquals(JsonNode.Parse(document.Text), original), "Stylesheet undo failed to restore original");
    var sheet = window.GetVisualDescendants().OfType<StyleSheetView>().Single();
    ((TabControl)sheet.Content!).SelectedIndex = 1;
    Wait(window, () => window.GetVisualDescendants().OfType<TreeView>().Any(tree => tree.Name == "DataTree"));
    var objects = window.GetVisualDescendants().OfType<JsonDataView>().Single();
    objects.ShowObject("!10001");
    var tree = window.GetVisualDescendants().OfType<TreeView>().Single(tree => tree.Name == "DataTree");
    var root = (JsonDataView.Node)tree.Items[0]!;
    tree.SelectedItem = root.Children.Single(node => node.Path.Last() == "styles").Children[1];
    var reference = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenObjectReference");
    Check(reference.IsEnabled && ((JsonDataView.Node)tree.SelectedItem!).Label.Contains("title · TextStyle"), "Object references did not resolve descriptive names");
    reference.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ObjectLocation").Text!.Contains("!10003"), "Reference navigation opened the wrong object");
    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ObjectBack").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ObjectLocation").Text!.Contains("!10001"), "Object back navigation failed");
    Check(!document.IsDirty, "Object navigation changed data");
    ((TabControl)sheet.Content!).SelectedIndex = 0;
    var textPresentation = window.GetVisualDescendants().OfType<TextPresentationView>().Single();
    textPresentation.ShowSource();
    document.Text = "{";
    textPresentation.SelectedIndex = 0;
    Wait(window, () => list.Items.Count == 0);
    document.Undo();
    Wait(window, () => list.Items.Count == 2 && window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "StyleName").Text == "default");
    Check(!document.IsDirty, "Stylesheet invalid source recovery lost undo history");
    presentation.Dispose();
    window.Close();
    Console.WriteLine("PASS stylesheet edits, original references, unknown fields, undo and object navigation");
}

static void SyntaxHighlighting()
{
    void Color(string name, string type, string source, int line, string token, string expected)
    {
        var syntax = AssetSyntax.ForSource(name, type, source);
        Check(syntax != null, "Missing syntax for " + name);
        var document = new AvaloniaEdit.Document.TextDocument(source);
        using var highlighter = new DocumentHighlighter(document, syntax!);
        var highlighted = highlighter.HighlightLine(line);
        var offset = document.GetLineByNumber(line).Offset + document.GetText(document.GetLineByNumber(line)).IndexOf(token, StringComparison.Ordinal);
        Check(highlighted.Sections.Any(section => section.Offset <= offset && offset < section.Offset + section.Length && section.Color.Name == expected), $"{syntax!.Name}: {token} was not highlighted as {expected}");
        Check(document.Text == source, "Highlighting changed the source");
    }
    const string lua = "--[=[ local ignored\nreturn 2\n]=]\nlocal message = \"안녕\\\"Lua\" -- if\nlocal raw = [=[\nif true then\n]=]\nreturn 0x1.fp2";
    Color("component", "com_script", lua, 2, "return", "Comment");
    Color("component", "com_script", lua, 4, "local", "Keyword");
    Color("component", "com_script", lua, 4, "안녕", "String");
    Color("component", "com_script", lua, 4, "\\", "Escape");
    Color("component", "com_script", lua, 4, "if", "Comment");
    Color("component", "com_script", lua, 6, "if", "String");
    Color("component", "com_script", lua, 8, "0x1.fp2", "Number");
    const string glsl = "#version 330\n/* uniform float ignored\nvec4 ignored; */\nuniform vec4 tint;\nvoid main() { gl_FragColor = vec4(1.0); }";
    Color("effect.glsl", "glsl", glsl, 1, "#version", "Directive");
    Color("effect.glsl", "glsl", glsl, 3, "vec4", "Comment");
    Color("effect.glsl", "glsl", glsl, 4, "uniform", "Keyword");
    Color("effect.glsl", "glsl", glsl, 4, "vec4", "Type");
    Color("effect.glsl", "glsl", glsl, 5, "main", "Function");
    const string shader = "local enabled = true\nsource 'fragment' [=[\n@in LOWP vec4 color;\nvoid main() { discard; }\n]=]\nreturn enabled";
    Color("effect", "shader_script", shader, 1, "local", "Keyword");
    Color("effect", "shader_script", shader, 3, "@in", "Directive");
    Color("effect", "shader_script", shader, 3, "LOWP", "Keyword");
    Color("effect", "shader_script", shader, 3, "vec4", "Type");
    Color("effect", "shader_script", shader, 4, "discard", "Keyword");
    Color("effect", "shader_script", shader, 6, "return", "Keyword");
    const string sq = "!start\n@hero\n#animation(idle)\n$duration:1\nif flag\n\tsay:안녕 // comment $wait(1)";
    Color("event", "sq_script", sq, 1, "!start", "Label");
    Color("event", "sq_script", sq, 2, "@hero", "Context");
    Color("event", "sq_script", sq, 3, "#animation", "Tag");
    Color("event", "sq_script", sq, 4, "$duration", "Directive");
    Color("event", "sq_script", sq, 5, "if", "Keyword");
    Color("event", "sq_script", sq, 6, "say", "Function");
    Color("event", "sq_script", sq, 6, "comment", "Comment");
    Color("event", "sq_script", sq, 6, "$wait", "Directive");
    const string json = "{\"한글\":\"안녕\\n\",\"amount\":-3.14e2,\"ok\":true}";
    Color("definition", "material", json, 1, "한글", "FieldName");
    Color("definition", "material", json, 1, "안녕", "String");
    Color("definition", "material", json, 1, "\\n", "Escape");
    Color("definition", "material", json, 1, "-3.14e2", "Number");
    const string xml = "<graphml>\n<!-- <node>\ncomment -->\n<node id=\"hero\"/>\n</graphml>";
    Color("graph", "quest_scheme", xml, 3, "comment", "Comment");
    Color("graph", "quest_scheme", xml, 4, "node", "Type");
    Color("graph", "quest_scheme", xml, 4, "id", "FieldName");
    Color("graph", "quest_scheme", xml, 4, "hero", "String");
    Color("font.fnt", "file", "char id=65 xadvance=12", 1, "id", "FieldName");
    Color("mesh.obj", "file", "v 1.0 -2 3", 1, "v", "Keyword");
    Color("sheet.csv", "file", "1,\"안녕\"\"World\",3", 1, "안녕", "String");
    Color("sheet.csv", "file", "1,\"안녕\"\"World\",3", 1, "\"\"", "Escape");
    Check(AssetSyntax.ForSource("notes.txt", "text", "ordinary text") == null, "Plain text was treated as code");
    Check(AssetSyntax.ForSource("unknown", "file", "<root/>")?.Name == "XML", "Serialized XML was not detected");
    Console.WriteLine("PASS Lua, GLSL, mixed shader sources, SQ, JSON, XML, BMFont, OBJ and CSV highlighting, multiline states, escapes and unchanged source");
}

static bool SourceReady(Window window, Func<bool> ready)
{
    foreach (var view in window.GetVisualDescendants().OfType<TextPresentationView>())
    {
        view.ShowSource();
    }

    return ready();
}

static IEnumerable<MenuItem> MenuItems(Window window)
{
    static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Descendants(item.Items))
            {
                yield return child;
            }
        }
    }
    return Descendants(window.GetVisualDescendants().OfType<Menu>().Single(menu => menu.Name == "MainMenu").Items);
}

static void Wait(Window window, Func<bool> ready)
{
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!ready())
    {
        using var dispatch = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(dispatch.Token);
        using var capture = window.CaptureRenderedFrame();
        if (DateTime.UtcNow > deadline)
        {
            throw new Exception("UI operation timed out");
        }

        Thread.Sleep(10);
    }
}

static void BundledLua()
{
    using var native = new NativeLuaDecompiler();
    Check(!native.IsLoaded, "Constructing a decompiler loaded the native library");
    var plain = new LuaAsset("return '한글'"u8.ToArray(), native.Decompile);
    Check(plain.GetSource().Contains("한글") && !native.IsLoaded, "Plain Lua loaded the native decompiler");
    var bytecode = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "branch.ljc"));
    var asset = new LuaAsset(bytecode, native.Decompile);
    var source = asset.GetSource();
    Check(native.IsLoaded && source.Contains("if") && source.Contains("return") && asset.Encode().SequenceEqual(bytecode) && asset.GetSource() == source, "Bundled decompiler failed, changed bytecode, or lost its cached source");
    Reject(() => native.Decompile([]));
    Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", "luajit-decompiler-MIT.txt")), "Bundled native license was not copied with the application");
    Console.WriteLine("PASS bundled native Lua source, lazy loading, plain Lua, bytecode preservation, cache and license packaging");
}

static void DeckPreview()
{
    var atlas = new RgbaImage(2, 2, [255, 0, 0, 128, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]);
    var mesh = JsonNode.Parse("""{"verts":[[0,0,0],[2,0,0],[2,2,0],[0,2,0]],"uv":[[0,1],[1,1],[1,0],[0,0]]}""")!;
    var definition = new JsonObject { ["name"] = "test", ["type"] = "deck2d.mquad", ["meshes"] = new JsonArray(mesh) };
    var original = definition.ToJsonString();
    var deck = new DeckAsset(definition, new Dictionary<string, RgbaImage> { ["Color"] = atlas, ["Normal"] = new RgbaImage(2, 2, Enumerable.Repeat(new byte[] { 128, 128, 255, 255 }, 4).SelectMany(pixel => pixel).ToArray()) });
    var image = deck.Render()!;
    Check(image.Width == 2 && image.Height == 3 && image.Pixels[0] == 255 && image.Pixels[3] == 128 && image.Pixels[5] == 255 && image.Pixels[10] == 255, "Deck UV orientation, geometry or triangle-edge alpha is wrong");
    Check(deck.Render("Part 1")!.Pixels.SequenceEqual(image.Pixels) && deck.Render("Composite", "Normal")!.Pixels[0] == 128, "Deck part or normal channel preview is wrong");
    Check(definition.ToJsonString() == original && deck.ToJson().Contains("test"), "Deck preview changed its source definition");
    var quads = new DeckAsset(JsonNode.Parse("""{"quads":[{"index":7,"rect":[0,0,2,2],"uv":[0,1,1,0]}]}""")!, deck.Textures);
    Check(quads.Entries.Single() == "7" && quads.Render("7")!.Pixels.SequenceEqual(image.Pixels), "Indexed quad rendering differs from the original quad geometry");
    var tile = new DeckAsset(new JsonObject { ["groups"] = new JsonArray(new JsonObject { ["tiles"] = new JsonArray(new JsonObject { ["name"] = "terrain.tile", ["meshes"] = new JsonArray(mesh.DeepClone()) }) }) }, deck.Textures);
    Check(tile.Entries.Single() == "terrain.tile" && tile.Render("terrain.tile")!.Pixels.SequenceEqual(image.Pixels), "Mesh tileset did not expose or render its tiles");
    Console.WriteLine("PASS deck UVs, XY geometry, nearest pixels, shared-edge alpha, parts, normal maps, indexed quads and mesh tiles");
}

static void DocumentNavigation(string directory)
{
    using var session = new DocumentSession();
    Directory.CreateDirectory(directory);
    using var workspace = new GameWorkspace(Fixture(directory), directory);
    var first = (TextDocument)workspace.OpenAsset("actors/hero/readme.txt");
    var second = (TextDocument)workspace.OpenAsset("actors/enemy/readme.txt");
    var third = (TextDocument)workspace.OpenAsset("settings.json");
    session.Open(first);
    session.Open(second);
    session.Open(first);
    Check(session.Documents.Count == 2 && session.CanGoBack && !session.CanGoForward, "Revisiting a document duplicated its tab");
    session.GoBack();
    Check(ReferenceEquals(session.SelectedDocument, second) && session.CanGoForward, "Back history selected the wrong same-named asset");
    session.Open(third);
    Check(!session.CanGoForward, "New navigation did not discard the forward branch");
    first.Text = "draft";
    session.TogglePin(first.Name);
    session.Close(first.Name);
    Check(first.IsDirty && session.PinnedNames.Contains(first.Name) && session.CanReopen, "Closing discarded a draft or pin");
    session.Reopen();
    Check(ReferenceEquals(session.SelectedDocument, first) && first.Text == "draft", "Reopen replaced the cached document");
    session.Close(first.Name);
    session.GoBack();
    Check(ReferenceEquals(session.SelectedDocument, first) && session.Documents.Contains(first), "History could not reopen a closed tab");
    session.TogglePin(first.Name);
    Check(session.PinnedNames.Count == 0, "Unpin did not remove the selected asset");
    foreach (var document in session.Documents.ToArray())
    {
        session.Close(document.Name);
    }

    Check(session.SelectedDocument == null && session.Documents.Count == 0 && first.IsDirty, "Closing all tabs discarded edits or retained a selection");
    session.Reopen();
    Check(session.SelectedDocument != null, "Could not reopen after closing all tabs");
    var changes = 0;
    session.Changed += (_, _) => changes++;
    session.Dispose();
    first.Text = "after session";
    Check(changes == 0 && session.Documents.Count == 0 && !session.CanGoBack && !session.CanGoForward, "Disposed session kept document subscriptions or history");
    workspace.Dispose();
    Directory.Delete(directory, true);
    Console.WriteLine("PASS document session identity, history branches, closed draft/pin preservation and disposal");
}

static void Navigation()
{
    var index = AssetIndex.Create("""
        {
            "actors":{"type":"folder"},
            "empty":{"type":"folder"},
            "actors/hero/readme.txt":{"type":"text","filePath":"game/asset/actors/hero/readme.txt"},
            "actors/enemy/readme.txt":{"type":"text"},
            "actors/hero/sprite.msprite":{"type":"msprite"},
            "actors/hero/sprite.msprite/atlas":{"type":"texture"},
            "virtual/idle":{"type":"deck2d.msprite_seq","parent":"actors/hero/sprite.msprite"}
        }
        """);
    var browser = new AssetBrowser(index);
    Check(browser.Mode == AssetBrowseMode.Directories && browser.Roots.Any(node => node.Name == "empty") && browser.MatchingCount == 5, "Gii tree lost empty folders or counted folders as assets");
    browser.Select("actors/enemy/readme.txt");
    var directory = browser.SelectedNode!.Parent!;
    Check(directory.Name == "enemy" && directory.IsExpanded && directory.Parent!.Name == "actors", "Directory ancestry did not follow project paths");
    browser.Update(AssetBrowseMode.Types);
    Check(browser.SelectedAsset!.AssetName == "actors/enemy/readme.txt" && browser.SelectedNode!.Parent!.Name == "text", "Type grouping lost full-path selection");
    browser.Select("virtual/idle");
    Check(browser.SelectedNode!.Parent!.Asset!.AssetName == "actors/hero/sprite.msprite", "Virtual asset did not follow its declared owner in type view");
    browser.Update(AssetBrowseMode.Directories, "idle");
    Check(browser.MatchingCount == 1 && browser.SelectedNode!.Parent!.Asset!.Type == "msprite" && browser.SelectedNode.Parent.Parent!.Name == "hero", "Filtered virtual asset lost its owner or project ancestry");
    browser.Update(AssetBrowseMode.Types, "", "texture");
    Check(browser.MatchingCount == 1 && browser.SelectedNode == null && browser.SelectedAsset!.AssetName == "virtual/idle", "Type filtering discarded the selected asset");
    browser.Update(AssetBrowseMode.Directories);
    Check(browser.SelectedNode!.Asset!.AssetName == "virtual/idle", "Clearing filters did not restore selection");
    browser.Roots.Single(node => node.Name == "empty").IsExpanded = true;
    browser.Update(AssetBrowseMode.Types);
    browser.Update(AssetBrowseMode.Directories);
    Check(browser.Roots.Single(node => node.Name == "empty").IsExpanded, "View switch lost directory expansion state");
    Check(index["actors/hero/readme.txt"].FilePath == "game/asset/actors/hero/readme.txt", "Navigation modified metadata paths");
    Console.WriteLine("PASS logical Gii paths, declared virtual parents, type groups, folder expansion and stable filtered selection");
}

static void ModManagerUI(string output)
{
    var service = new FakeModManagerClient();
    var directory = Path.Combine(Path.GetTempPath(), "Eastward-mod-ui-fixture");
    service.NormalizedDirectory = Path.Combine(directory, "content", "game");
    var view = new ModManagerView(directory, service);
    view.Show();
    Button Button(string name) => view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
    void Click(string name) => Button(name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    CheckBox Toggle(string name) => view.GetVisualDescendants().OfType<CheckBox>().Single(check => check.Content is TextBlock label && label.Text == name);
    Wait(view, () => Button("InstallLoader").IsEnabled);
    Check(service.InstallCalls == 0 && !service.Installed && view.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "ModInstallation").Text == service.NormalizedDirectory, "Opening mod manager installed automatically or did not display the normalized installation");
    Click("InstallLoader");
    Wait(view, () => Button("UninstallLoader").IsEnabled && service.InstallCalls == 1);
    Toggle("Fixture resource mod").IsChecked = false;
    Toggle("Conflicting resource mod").IsChecked = true;
    Check(!Button("ApplyModActivation").IsEnabled && service.ApplyCalls == 0, "Conflicting enabled mod was allowed or toggles wrote immediately");
    Toggle("Conflicting resource mod").IsChecked = false;
    Check(Button("ApplyModActivation").IsEnabled, "Disabling conflicting mod did not unblock activation");
    service.FailApply = true;
    Click("ApplyModActivation");
    Wait(view, () => view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "ModManagerStatus" && block.Text == "Fixture managed-file validation failed"));
    Check(!Toggle("Fixture resource mod").IsChecked!.Value && service.ApplyCalls == 0, "Failed validation lost pending toggles or wrote activation");
    service.FailApply = false;
    Click("ApplyModActivation");
    Wait(view, () => service.ApplyCalls == 1 && Button("AddModFile").IsEnabled && !Button("ApplyModActivation").IsEnabled);
    var added = view.AddSource("fixture-mod.lua");
    Wait(view, () => added.IsCompleted && view.GetVisualDescendants().OfType<CheckBox>().Count() == 3);
    Check(service.AddedSource == "fixture-mod.lua", "Mod manager did not forward selected Lua file");
    view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RemoveMod" && Equals(ToolTip.GetTip(button), "lua")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Wait(view, () => service.RemovedId == "lua" && view.GetVisualDescendants().OfType<CheckBox>().Count() == 2);
    view.Width = 460;
    view.Height = 470;
    Wait(view, () => view.GetVisualDescendants().OfType<ScrollViewer>().Any(scroll => scroll.Name == "ModManagerScroll" && scroll.Extent.Height > scroll.Viewport.Height));
    var apply = Button("ApplyModActivation");
    var location = apply.TranslatePoint(default, view);
    Check(location != null && location.Value.Y + apply.Bounds.Height <= view.Bounds.Height, "Mod manager apply button was clipped");
    using (var image = view.CaptureRenderedFrame())
    {
        image!.Save(Path.Combine(output, "mod-manager-narrow.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    service.FileConflicts = ["script/lib.lua changed outside the loader"];
    Click("RefreshMods");
    Wait(view, () => view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "ModManagerStatus" && block.Text!.StartsWith("Conflicts:")));
    Check(!Button("InstallLoader").IsEnabled && Button("UninstallLoader").IsEnabled && !Button("ApplyModActivation").IsEnabled, "Conflicts blocked recovery or enabled an unsafe install");
    Click("UninstallLoader");
    Wait(view, () => view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "ModManagerStatus" && block.Text == "Fixture bootstrap checksum mismatch"));
    Check(service.UninstallCalls == 0 && service.Installed, "Uninstall checksum validation changed fixture state");
    service.FileConflicts = [];
    Click("RefreshMods");
    Wait(view, () => Button("UninstallLoader").IsEnabled);
    Click("UninstallLoader");
    Wait(view, () => service.UninstallCalls == 1 && Button("InstallLoader").IsEnabled);
    Check(service.LastDirectory == Path.GetFullPath(service.NormalizedDirectory) && service.ApplyCalls == 1, "Mod manager used a different installation target or performed extra writes");
    view.Close();
    var root = Path.Combine(Path.GetTempPath(), "Eastward-mod-menu-" + Guid.NewGuid());
    Directory.CreateDirectory(root);
    try
    {
        Fixture(root);
        var menuService = new FakeModManagerClient();
        var main = new MainWindow(["--game", root], () => menuService);
        main.Show();
        var modMenu = main.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Mod"));
        var command = modMenu.Items.OfType<MenuItem>().Single(item => item.Name == "ManageModsMenu");
        Wait(main, () => command.IsEnabled);
        command.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Wait(main, () => main.OwnedWindows.OfType<ModManagerView>().Any());
        var dialog = main.OwnedWindows.OfType<ModManagerView>().Single();
        Wait(dialog, () => dialog.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "InstallLoader" && button.IsEnabled));
        Check(menuService.InstallCalls == 0 && menuService.LastDirectory == Path.GetFullPath(root), "Mod menu manager inspected a different target or installed automatically");
        dialog.Close();
        Wait(main, () => command.IsEnabled);
        main.Close();
    }
    finally
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fixture path left the temporary directory");
        }

        Directory.Delete(root, true);
    }
    Console.WriteLine("PASS mod manager read-only opening, install/update, pending activation, conflicts, failed validation recovery, Lua import, narrow layout and uninstall through fixture client; real game was not changed");
}

static void LanguagePatchUI(string output)
{
    var root = Path.Combine(Path.GetTempPath(), "Eastward-language-ui-" + Guid.NewGuid());
    Directory.CreateDirectory(root);
    try
    {
        using var workspace = new GameWorkspace(Fixture(root), root);
        var owner = new Window { Width = 800, Height = 700 };
        owner.Show();
        var dialog = new LanguagePatchView(workspace);
        var result = dialog.ShowDialog<IReadOnlyList<AssetDocument>?>(owner);
        Wait(dialog, () => dialog.GetVisualDescendants().OfType<TextBox>().Any(input => input.Name == "PatchLanguage"));
        TextBox Input(string name) => dialog.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == name);
        Input("PatchLanguage").Text = "ko";
        Input("PatchDisplayName").Text = "한국어";
        Input("PatchTranslations").Text = "translations.json";
        Input("PatchOriginalFonts").Text = "font/Dialog.fnt\nfont/ui.fnt";
        Input("PatchDialogLineSpacing").Text = "1.25";
        Input("PatchFirstRunSpacing").Text = "36";
        dialog.AddSource("texture.png", "chapter.png");
        dialog.AddSource("props.deckpack", "edited-decks");
        var options = dialog.ReadOptions();
        Check(options.Language == "ko" && options.DisplayName == "한국어" && options.BaseLanguage == "en" && options.LocalizedImages["texture.png"] == "chapter.png" && options.SourceFiles["props.deckpack"] == "edited-decks" && options.TranslationFile == "translations.json" && options.IncludeInitialMenu && options.OriginalFontAssets.Count == 2 && options.DialogLineSpacing == 1.25 && options.FirstRunButtonSpacing == 36, "Language patch form lost Unicode, graphics mapping or default options");
        using (var wideImage = dialog.CaptureRenderedFrame())
        {
            wideImage!.Save(Path.Combine(output, "language-patch-wide.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        dialog.Width = 460;
        dialog.Height = 510;
        Wait(dialog, () => dialog.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "CreateLanguagePatch" && button.Bounds.Width > 0));
        var create = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CreateLanguagePatch");
        Check(dialog.GetVisualDescendants().OfType<ScrollViewer>().Any(scroll => scroll.Name == "LanguagePatchScroll" && scroll.Extent.Height > scroll.Viewport.Height), "Narrow language patch form did not scroll");
        var position = create.TranslatePoint(default, dialog);
        Check(position != null && position.Value.Y >= 0 && position.Value.Y + create.Bounds.Height <= dialog.Bounds.Height, "Language patch create button was clipped in a narrow window");
        var prepared = workspace.OpenAsset("settings.json");
        dialog.CreatePatch = _ => throw new InvalidDataException("Fixture error");
        create.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(dialog, () => dialog.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "LanguagePatchStatus" && block.Text == "Fixture error"));
        Check(Input("PatchDisplayName").Text == "한국어" && !workspace.HasChanges, "Failed language patch creation lost input or changed drafts");
        dialog.CreatePatch = input => { Check(input.LocalizedImages["texture.png"] == "chapter.png", "Create action lost source mapping"); return new[] { prepared }; };
        using var image = dialog.CaptureRenderedFrame();
        image!.Save(Path.Combine(output, "language-patch-narrow.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(view => view.Name == "LanguagePatchScroll");
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        using (var bottomImage = dialog.CaptureRenderedFrame())
        {
            bottomImage!.Save(Path.Combine(output, "language-patch-options.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        create.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(owner, () => result.IsCompleted);
        Check(result.Result?.Single() == prepared, "Language patch dialog did not return workspace documents");
        owner.Close();
        var main = new MainWindow(["--game", root]);
        main.Show();
        var modMenu = main.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Mod"));
        var command = modMenu.Items.OfType<MenuItem>().Single(item => item.Name == "CreateLanguagePatchMenu");
        Wait(main, () => command.IsEnabled);
        command.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Wait(main, () => main.OwnedWindows.OfType<LanguagePatchView>().Any());
        var menuDialog = main.OwnedWindows.OfType<LanguagePatchView>().Single();
        menuDialog.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "PatchLanguage").Text = "ko";
        menuDialog.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "PatchDisplayName").Text = "한국어";
        menuDialog.CreatePatch = _ => new[] { prepared };
        menuDialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CreateLanguagePatch").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var drafts = modMenu.Items.OfType<MenuItem>().Single(item => item.Name == "LanguagePatchDrafts");
        Wait(main, () => drafts.IsEnabled && drafts.Items.Count == 1);
        Check(Equals(((MenuItem)drafts.Items[0]!).Header, "settings.json"), "Mod menu did not expose created patch drafts");
        Wait(main, () => main.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "DocumentTab" && Equals(ToolTip.GetTip(button), "settings.json")));
        var restoredRoot = Path.Combine(root, "restored");
        Directory.CreateDirectory(restoredRoot);
        using var sourceWorkspace = new GameWorkspace(Fixture(restoredRoot), restoredRoot);
        var restoredScript = (TextDocument)sourceWorkspace.OpenAsset("fixture.lua");
        restoredScript.Text = "-- EastwardModTool.LanguagePatch.v1\nreturn true";
        var projectPath = Path.Combine(root, "language-ui.ewproj");
        WorkspaceProject.Save(sourceWorkspace, projectPath);
        var restoredWorkspace = WorkspaceProject.Open(projectPath);
        typeof(MainWindow).GetMethod("SetWorkspace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(main, [restoredWorkspace]);
        Wait(main, () => drafts.IsEnabled && drafts.Items.Count == 1);
        Check(Equals(((MenuItem)drafts.Items[0]!).Header, "fixture.lua"), "Reopening a project must restore language patch draft navigation");
        ((MenuItem)drafts.Items[0]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Wait(main, () => main.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "DocumentTab" && Equals(ToolTip.GetTip(button), "fixture.lua")));
        main.Close();
        Console.WriteLine("PASS language patch input, local/shared image mapping, narrow scroll/fixed buttons, error recovery, Mod menu dialog, editing draft tabs and reopened project navigation");
    }
    finally { Directory.Delete(root, true); }
}

static void LanguageImages(string directory, string output)
{
    var gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    output = Path.GetFullPath(output);
    if (output.Equals(gameRoot, StringComparison.OrdinalIgnoreCase) || output.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Verification output must be outside the game installation");
    }

    if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    {
        throw new IOException("Use an empty verification output directory");
    }

    Directory.CreateDirectory(output);
    using var workspace = GameWorkspace.Open(directory);
    var assets = workspace.Assets.Values.ToArray();
    var changed = new List<string>();
    static RgbaImage ChangeImage(RgbaImage original)
    {
        var pixels = (byte[])original.Pixels.Clone();
        var offset = Enumerable.Range(0, pixels.Length / 4).First(index => pixels[index * 4 + 3] > 0) * 4;
        pixels[offset] = (byte)(pixels[offset] ^ 127);
        return new RgbaImage(original.Width, original.Height, pixels);
    }
    var textureInfo = assets.First(info => info.Type == "texture" && info.AssetName.Contains("ChapterTitle/icon/", StringComparison.Ordinal));
    var texture = (ResourceDocument)workspace.OpenAsset(textureInfo.AssetName);
    var originalTexture = texture.GetImage()!;
    var imagePath = Path.Combine(output, "chapter-icon.png");
    ChangeImage(originalTexture).SavePng(imagePath);
    texture.ImportSource(imagePath);
    Check(texture.GetImage()!.Width == originalTexture.Width && !texture.GetImage()!.Pixels.SequenceEqual(originalTexture.Pixels), "Chapter logo PNG import did not update image pixels");
    changed.Add(texture.Name);
    var packInfo = assets.First(info => info.Type == "texture_pack");
    var packDocument = (ResourceDocument)workspace.OpenAsset(packInfo.AssetName);
    var pack = (TexturePackAsset)packDocument.Baseline;
    var images = pack.Textures.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var layer = images.First(pair => pair.Value.Pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0));
    images[layer.Key] = ChangeImage(layer.Value);
    var psd = Path.Combine(output, "texture-pack.psd");
    new TexturePackAsset(images).SaveTo(psd);
    packDocument.ImportSource(psd);
    Check(!((TexturePackAsset)packDocument.Current).Textures[layer.Key].Pixels.SequenceEqual(layer.Value.Pixels), "Texture pack PSD import did not update selected layer");
    changed.Add(packDocument.Name);
    var deckDocument = assets.Where(info => info.Type == "deck_pack").Select(info => workspace.OpenAsset(info.AssetName)).OfType<ResourceDocument>().First(document => document.Baseline is DeckPackAsset source && ((HmgAsset)source.Items["decks.png"]).Image!.Pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0));
    var deck = (DeckPackAsset)deckDocument.Baseline;
    var deckFolder = Path.Combine(output, "deck-pack");
    deckDocument.ExportSource(deckFolder);
    var deckImage = (HmgAsset)deck.Items["decks.png"];
    ChangeImage(deckImage.Image!).SavePng(Path.Combine(deckFolder, "decks.png"));
    deckDocument.ImportSource(deckFolder);
    Check(JsonNode.DeepEquals(((JsonAsset)((DeckPackAsset)deckDocument.Current).Items["decks.json"]).Data, ((JsonAsset)deck.Items["decks.json"]).Data), "Deck atlas edit changed geometry or UV definitions");
    changed.Add(deckDocument.Name);
    ResourceDocument? spriteDocument = null;
    foreach (var info in assets.Where(info => info.Type == "msprite"))
    {
        var document = workspace.OpenAsset(info.AssetName);
        if (document is ResourceDocument candidate && candidate.Baseline is MSpriteAsset sprite && sprite.Data!["anims"]!.AsObject().Count == 1 && sprite.Data["atlases"]!.AsObject().Count == 1 && sprite.Data["anims"]!.AsObject().First().Value!["seq"]!.AsArray().Count is > 0 and <= 32)
        {
            spriteDocument = candidate;
            break;
        }
    }
    Check(spriteDocument != null, "No small single-animation installed sprite available");
    var originalSprite = (MSpriteAsset)spriteDocument!.Baseline;
    var spriteFolder = Path.Combine(output, "sprite");
    spriteDocument.ExportSource(spriteFolder);
    var atlas = originalSprite.AtlasImages.First();
    var modifiedAtlases = originalSprite.AtlasImages.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    // Recolour opaque source pixels so at least one used sprite module changes.
    var spritePixels = (byte[])atlas.Value.Pixels.Clone();
    for (var pixel = 0; pixel < spritePixels.Length; pixel += 4)
    {
        if (spritePixels[pixel + 3] > 0)
        {
            spritePixels[pixel] ^= 127;
        }
    }

    modifiedAtlases[atlas.Key] = new RgbaImage(atlas.Value.Width, atlas.Value.Height, spritePixels);
    File.WriteAllBytes(Directory.GetFiles(spriteFolder, "*.ase").Single(), AseWriter.Write(originalSprite.Data!, modifiedAtlases, []));
    spriteDocument.ImportSource(spriteFolder);
    Check(!((MSpriteAsset)spriteDocument.Current).RenderFrame(spriteDocument.Entries[0], 0).Pixels.SequenceEqual(originalSprite.RenderFrame(spriteDocument.Entries[0], 0).Pixels), "Sprite ASE import did not change the displayed frame");
    changed.Add(spriteDocument.Name);
    var mod = workspace.BuildMod("Language image workflow verification");
    var modFile = Path.Combine(output, "language-images.ewmod");
    mod.SaveTo(modFile);
    var read = ModPackage.Read(modFile);
    Check(workspace.ValidateMod(read).Count == 0, "Image mod baseline validation failed");
    var destination = Path.Combine(output, "archives");
    workspace.SaveArchives(destination);
    foreach (var path in read.ResourcePaths)
    {
        var bytes = read.GetResource(path);
        var archive = GArchive.Read(Path.Combine(destination, path.Split('/')[0] + ".g"));
        Check(archive[path].SequenceEqual(bytes), "Image archive output differs from packaged resource");
        if (bytes.AsSpan().StartsWith("PGF"u8))
        {
            Check(Asset.Create<HmgAsset>(bytes).Image != null, "Compiled HMG resource could not be decoded");
        }
    }
    using var reopened = GameWorkspace.Open(directory, destination);
    Check(!((ResourceDocument)reopened.OpenAsset(texture.Name)).GetImage()!.Pixels.SequenceEqual(originalTexture.Pixels), "Written archive overlay did not expose the edited chapter image");
    Check(!((ResourceDocument)reopened.OpenAsset(spriteDocument.Name)).GetImage(spriteDocument.Entries[0])!.Pixels.SequenceEqual(originalSprite.RenderFrame(spriteDocument.Entries[0], 0).Pixels), "Written archive overlay did not expose the edited sprite frame");
    foreach (var name in changed)
    {
        Console.WriteLine("IMAGE " + name);
    }

    Console.WriteLine($"PASS installed PNG/HMG, texture-pack PSD, deck atlas and sprite ASE imports → BuildMod → {read.ResourcePaths.Count} runtime resources → independent archive output and reload; game was not started");
}

static void InstalledGame(string directory, string? screenshot)
{
    var root = Path.Combine(Path.GetTempPath(), "EastwardModTool-installed-" + Guid.NewGuid());
    Directory.CreateDirectory(root);
    string scriptName;
    string originalScript;
    try
    {
        using (var workspace = GameWorkspace.Open(directory))
        {
            scriptName = workspace.Assets.Values.First(info => info.Type == "com_script").AssetName;
            var script = (TextDocument)workspace.OpenAsset(scriptName);
            originalScript = script.Text;
            script.Text += "\n-- EastwardModTool source round trip\n";
            var mod = workspace.BuildMod("Installed resource verification");
            var modPath = Path.Combine(root, "installed.ewmod");
            mod.SaveTo(modPath);
            Check(workspace.ValidateMod(ModPackage.Read(modPath)).Count == 0, "Installed mod does not match its original resources");
            workspace.SaveArchives(Path.Combine(root, "archives"));
            foreach (var path in mod.ResourcePaths)
            {
                var archive = GArchive.Read(Path.Combine(root, "archives", path.Split('/')[0] + ".g"));
                Check(archive[path].SequenceEqual(mod.GetResource(path)), "Installed archive did not preserve the compiled edit");
            }
            Console.WriteLine("PASS installed mod package and full affected archive writing");
            foreach (var info in workspace.Assets.Values.Where(info => info.Type == "mesh"))
            {
                var document = (ResourceDocument)workspace.OpenAsset(info.AssetName);
                var source = Path.Combine(root, "mesh-" + Guid.NewGuid().ToString("N"));
                document.ExportSource(source);
                document.ImportSource(source);
                var editedSource = source + "-edited";
                document.ExportSource(editedSource);
                document.ImportSource(editedSource);
                Check(document.Current is MeshAsset, "Installed mesh lost its source representation");
            }
            var project = Path.Combine(root, "installed.ewproject");
            WorkspaceProject.Save(workspace, project);
            using (var reopened = WorkspaceProject.Open(project))
            {
                Check(((TextDocument)reopened.OpenAsset(scriptName)).Text == script.Text, "Installed script draft did not restore");
                reopened.BuildMod("Reopened installed edits");
                Check(reopened.Documents.Count(document => document is ResourceDocument resource && resource.Current is MeshAsset) == 5, "Installed mesh drafts did not restore");
            }
            var luaCount = 0;
            foreach (var info in workspace.Assets.Values.Where(info => info.Type == "lua").Take(5))
            {
                Check(workspace.OpenAsset(info.AssetName) is TextDocument lua && lua.Text.Length > 0 && !lua.IsDirty, "Installed Lua did not open through the bundled decompiler: " + info.AssetName);
                luaCount++;
            }
            Check(luaCount == 5, "Not enough installed Lua assets tested");
            Console.WriteLine("PASS five installed Lua assets automatically decompiled without --decompiler");
            var deckCount = 0;
            foreach (var info in workspace.Assets.Values.Where(info => info.Type is "deck2d.mquad" or "deck2d.mtileset" or "deck2d.quads").GroupBy(info => info.Type).SelectMany(group => group.Take(4)))
            {
                var document = (ResourceDocument)workspace.OpenAsset(info.AssetName);
                Check(document.Current is DeckAsset && document.GetImage() is { } preview && preview.Pixels.Where((value, index) => index % 4 == 3).Any(value => value > 0), "Installed deck did not render its parent atlas: " + info.AssetName);
                Check(!document.CanEdit && !document.IsDirty, "Deck preview modified a virtual asset");
                if (deckCount == 0 && screenshot != null)
                {
                    document.GetImage()!.SavePng(Path.Combine(Path.GetDirectoryName(screenshot!)!, "deck-installed-preview.png"));
                }

                deckCount++;
            }
            Check(deckCount > 0, "No installed decks tested");
            var deckPackInfo = workspace.Assets.Values.First(info => info.Type == "deck_pack");
            var deckPack = (ResourceDocument)workspace.OpenAsset(deckPackInfo.AssetName);
            Check(deckPack.GetImage(deckPack.Entries[0]) != null, "Deck pack default entry is not an image");
            Console.WriteLine($"PASS {deckCount} installed deck previews and default pack image");
            var sequenceInfo = workspace.Assets.Values.First(info => info.Type == "deck2d.msprite_seq");
            var sequenceDocument = (ResourceDocument)workspace.OpenAsset(sequenceInfo.AssetName);
            Check(sequenceDocument.CanAnimate && sequenceDocument.GetImage() != null && sequenceDocument.GetFrameDuration(sequenceDocument.Entries[0], 0) > TimeSpan.Zero, "Virtual sprite sequence did not expose its parent animation");
            var spriteInfo = workspace.Assets.Values.First(info => info.Type == "msprite" && info.AssetName.Contains("mika", StringComparison.OrdinalIgnoreCase));
            var sprite = (ResourceDocument)workspace.OpenAsset(spriteInfo.AssetName);
            var animation = sprite.Entries.First(entry => sprite.GetFrameCount(entry) > 0);
            var bounds = ((MSpriteAsset)sprite.Current).GetPreviewBounds();
            var previewWatch = System.Diagnostics.Stopwatch.StartNew();
            var checkedFrames = 0;
            foreach (var name in sprite.Entries.Where(entry => sprite.GetFrameCount(entry) > 0).Take(10))
            {
                foreach (var frameIndex in new[] { 0, sprite.GetFrameCount(name) - 1 }.Distinct())
                {
                    var preview = sprite.GetImage(name, frameIndex)!;
                    Check(preview.Width == bounds.Width && preview.Height == bounds.Height, "Installed sprite changed its canvas between frames or animations");
                    checkedFrames++;
                }
            }
            Console.WriteLine($"PASS installed Mika shared origin ({-bounds.Left},{-bounds.Top}), canvas {bounds.Width}×{bounds.Height}, {checkedFrames} frames in {previewWatch.Elapsed.TotalSeconds:0.00}s");
            Console.WriteLine($"PASS installed workspace ({workspace.Assets.Count} entries), five mesh draft round trips and Mika frame rendering");
        }
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(["--game", directory]);
        window.Show();
        Wait(window, () => window.GetVisualDescendants().OfType<AssetBrowserView>().Single().Browser.MatchingCount > 0);
        var search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search assets…");
        var totalAssets = window.GetVisualDescendants().OfType<AssetBrowserView>().Single().Browser.MatchingCount;
        search.Text = scriptName;
        var list = window.GetVisualDescendants().OfType<AssetBrowserView>().Single();
        Check(list.Browser.Mode == AssetBrowseMode.Directories, "Installed asset browser did not default to project directories");
        Wait(window, () => list.Browser.MatchingCount > 0);
        list.SelectAsset(scriptName);
        Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => !box.IsReadOnly && box.Text == originalScript));
        var sourceEditor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        search.Text = "";
        Wait(window, () => list.Browser.MatchingCount == totalAssets);
        sourceEditor.CaretOffset = Math.Min(20, sourceEditor.Text.Length);
        var caret = sourceEditor.CaretOffset;
        var mode = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AssetViewMode");
        var switchTimer = System.Diagnostics.Stopwatch.StartNew();
        mode.SelectedIndex = 1;
        Wait(window, () => list.Browser.Mode == AssetBrowseMode.Types);
        using (var capture = window.CaptureRenderedFrame())
        {
            Check(list.SelectedAsset!.AssetName == scriptName && ReferenceEquals(sourceEditor, window.GetVisualDescendants().OfType<TextEditor>().Single()) && sourceEditor.CaretOffset == caret, "Installed type view lost selection or editor position");
            if (screenshot != null)
            {
                capture!.Save(Path.ChangeExtension(screenshot, ".types.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        Console.WriteLine($"PASS installed type view ({list.Browser.MatchingCount} assets) and stable selection in {switchTimer.Elapsed.TotalSeconds:F2}s");
        mode.SelectedIndex = 0;
        Wait(window, () => list.Browser.Mode == AssetBrowseMode.Directories);
        if (screenshot != null)
        {
            using var capture = window.CaptureRenderedFrame();
            capture!.Save(screenshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        var metadataTimer = System.Diagnostics.Stopwatch.StartNew();
        var index = MenuItems(window).Single(item => item.Name == "EditAssetIndex");
        index.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => !box.IsReadOnly && box.Text?.Contains("objectFiles") == true));
        using (var capture = window.CaptureRenderedFrame())
        {
            Check(capture != null, "Installed metadata editor did not render");
        }

        Console.WriteLine($"PASS installed global index editor rendered in {metadataTimer.Elapsed.TotalSeconds:F2}s");
        Wait(window, () => window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
        var deckName = list.Browser.Types.Contains("deck2d.mquad") ? list.Browser.Roots.SelectMany(DeckNodes).First(node => node.Asset?.Type == "deck2d.mquad").Asset!.AssetName : throw new InvalidDataException("No installed mesh deck");
        list.SelectAsset(deckName);
        Wait(window, () => window.GetVisualDescendants().OfType<TabControl>().Any(tabs => tabs.Name == "DeckViews") && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
        using (var capture = window.CaptureRenderedFrame())
        {
            Check(window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null), "Installed deck UI did not show its geometry preview");
            if (screenshot != null)
            {
                capture!.Save(Path.ChangeExtension(screenshot, ".deck.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        Console.WriteLine("PASS installed mesh deck preview UI");
        list.SelectAsset(list.SelectedAsset!.Parent);
        Wait(window, () => window.GetVisualDescendants().OfType<DeckPackEditorView>().Any() && window.GetVisualDescendants().OfType<Menu>().Single().IsEffectivelyEnabled);
        var parentList = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "PackDeckList");
        Check(parentList.Items.Count > 1 && window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null), "Installed PSD parent did not show its deck list and first preview");
        parentList.SelectedItem = parentList.Items[parentList.Items.Count - 1];
        using (var capture = window.CaptureRenderedFrame())
        {
            Check(window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null), "Selecting an installed parent deck lost its preview");
            if (screenshot != null)
            {
                capture!.Save(Path.ChangeExtension(screenshot, ".parent.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        Console.WriteLine($"PASS installed PSD parent with {parentList.Items.Count} selectable deck previews");
        window.Close();
        Console.WriteLine("PASS desktop installed game indexing, search, source loading and rendering; game was not started");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static IEnumerable<AssetTreeNode> DeckNodes(AssetTreeNode node)
{
    yield return node;
    foreach (var child in node.Children.SelectMany(DeckNodes))
    {
        yield return child;
    }
}

static void Check(bool value, string message)
{
    if (!value)
    {
        throw new Exception(message);
    }
}

static void Reject(Action action)
{
    try
    {
        action();
    }
    catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException or IOException) { return; }
    throw new Exception("Invalid input was accepted");
}

static AssetManager Fixture(string root)
{
    var assets = AssetIndex.Create("""
        {
          "hello.txt":{"type":"text","label":"한글 메모","objectFiles":{"data":"fixture/text"}},
          "actors":{"type":"folder"},
          "empty":{"type":"folder"},
          "actors/hero/readme.txt":{"type":"text","objectFiles":{"data":"fixture/hero"}},
          "actors/enemy/readme.txt":{"type":"text","objectFiles":{"data":"fixture/enemy"}},
          "settings.json":{"type":"data_json","objectFiles":{"data":"fixture/settings"}},
          "text.stylesheet":{"type":"stylesheet","objectFiles":{"def":"fixture/styles"}},
          "objects.scene":{"type":"scene","objectFiles":{"def":"fixture/scene","compiled":"fixture/scene-bin"}},
          "objects.prefab":{"type":"prefab","objectFiles":{"def":"fixture/prefab"}},
          "objects.material":{"type":"physics_material","objectFiles":{"def":"fixture/material"}},
          "fixture.lua":{"type":"lua"},
          "event.sq":{"type":"sq_script","objectFiles":{"data":"fixture/sq"}},
          "fsm.graphml":{"type":"fsm_scheme","objectFiles":{"def":"fixture/fsm"}},
          "texture.png":{"type":"texture","objectFiles":{"pixmap":"fixture/image"}},
          "mesh.obj":{"type":"mesh","objectFiles":{"mesh":"fixture/mesh"}},
          "sprite.msprite":{"type":"msprite","objectFiles":{"def":"fixture/sprite"}},
          "sprite.msprite/atlas":{"type":"texture","objectFiles":{"pixmap":"fixture/atlas"}},
          "sprite.msprite/idle":{"type":"deck2d.msprite_seq"},
          "props.deckpack":{"type":"deck_pack","objectFiles":{"export":"fixture/deck"}},
          "props.deckpack/box":{"type":"deck2d.mquad"},
          "props.deckpack/box2":{"type":"deck2d.mquad"}
        }
        """);
    var manager = new AssetManager(assets, ScriptLibrary.Create("{\"source\":{\"fixture\":\"fixture.lua\"},\"export\":{\"fixture\":\"fixture/lua\"}}"), TextureIndex.Create("{\"root\":\"1\",\"map\":{\"1\":{\"body\":{\"groups\":[]}}}}"));
    var archive = GArchive.Create("fixture");
    archive["fixture/deck/decks.json"] = """{"decks":[{"name":"box","type":"deck2d.mquad","meshes":[{"verts":[[0,0,0],[2,0,0],[2,2,0],[0,2,0]],"uv":[[0,1],[1,1],[1,0],[0,0]]}]},{"name":"box2","type":"deck2d.mquad","meshes":[{"verts":[[0,0,0],[4,0,0],[4,2,0],[0,2,0]],"uv":[[0,1],[1,1],[1,0],[0,0]]}]}]}"""u8.ToArray();
    archive["fixture/deck/decks.png"] = new RgbaImage(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]).ToPng();
    archive["fixture/deck/decks_n.png"] = new RgbaImage(2, 2, Enumerable.Repeat(new byte[] { 128, 128, 255, 255 }, 4).SelectMany(pixel => pixel).ToArray()).ToPng();
    archive["fixture/text"] = "original"u8.ToArray();
    archive["fixture/lua"] = [27, 76, 74, 2, 2, 11, 2, 0, 1, 0, 0, 0, 1, 75, 0, 1, 0, 0];
    archive["fixture/hero"] = "hero"u8.ToArray();
    archive["fixture/enemy"] = "enemy"u8.ToArray();
    var sceneData = """{"__VERSION":"2","entities":[{"id":"hero","components":["component"],"children":[{"id":"hero","components":[],"children":[]}]}],"map":{"hero":{"model":"mock.Entity","body":{"name":"Hero","loc":[1,2,3],"unknown":false}},"component":{"model":"mock.PhysicsBody","body":{"name":"Body","material":"material","target":"hero"}},"material":{"model":"mock.PhysicsMaterial","body":{"friction":0.5,"unknown":{"keep":42}}}},"unknown":"keep"}"""u8.ToArray();
    archive["fixture/scene/scene_index.json"] = """{"groups":[],"unknown":false}"""u8.ToArray();
    archive["fixture/scene/default.scene_group"] = sceneData;
    archive["fixture/scene/preserved.bin"] = [1, 2, 3];
    archive["fixture/prefab"] = sceneData;
    var materialData = JsonNode.Parse(sceneData)!;
    materialData["root"] = "material";
    archive["fixture/material"] = Encoding.UTF8.GetBytes(materialData.ToJsonString());
    archive["fixture/styles"] = """{"root":"!10001","map":{"!10001":{"model":"mock.StyleSheet","body":{"styles":["!10002","!10003"],"extra":42}},"!10002":{"model":"mock.TextStyle","body":{"name":"default","font":false,"size":16,"color":[1,0.5,0,1],"allowScale":true,"unknown":{"keep":false}}},"!10003":{"model":"mock.TextStyle","body":{"name":"title","font":"font/original.fnt","size":24,"color":[1,1,1,1],"allowScale":false}}},"unknown":false}"""u8.ToArray();
    archive["fixture/settings"] = "{\"unknown\":false,\"message\":\"안녕\"}"u8.ToArray();
    archive["fixture/sq"] = Encoding.UTF8.GetBytes(SQCompiler.Compile("!start\nsay hello", "event.sq").ToJsonString());
    archive["fixture/fsm"] = "return (function() nodelist={['start']={localName='start',type='state'};['next']={localName='next',type='state'};}; nodelist['start'].next={['$next']='next';[true]='next';}; return nodelist end) ()"u8.ToArray();
    archive["fixture/image"] = new RgbaImage(1, 1, [50, 60, 70, 128]).ToPng();
    archive["fixture/mesh/mesh"] = "<ASSIMP><Scene><Node><MeshRefs>0</MeshRefs></Node><MeshList><Mesh><Positions>0 0 0\n1 0 0\n0 1 0</Positions><Normals>0 0 1\n0 0 1\n0 0 1</Normals><FaceList><Face>0 1 2</Face></FaceList></Mesh></MeshList></Scene></ASSIMP>"u8.ToArray();
    archive["fixture/atlas"] = new RgbaImage(2, 1, [255, 0, 0, 128, 0, 255, 0, 128]).ToPng();
    archive["fixture/sprite"] = """
        {"atlases":{"a":"atlas"},"features":[],"anims":{"a":{"name":"idle","seq":[["f",0.1,2,-1],["g",0.2,99,-50]]}},"frames":{"f":{"parts":[["red",0,0],["green",0,0]],"meta":{"origin":[100,100]}},"g":{"parts":[["red",-2,-3],["green",2,1]],"meta":{}}},"modules":{"red":{"atlas":"a","rect":[0,0,1,1],"feature":0},"green":{"atlas":"a","rect":[1,0,1,1],"feature":0}}}
        """u8.ToArray();
    manager.LoadArchive(archive);
    var config = GArchive.Create("config");
    manager.SaveMetadata(config);
    config.Write(Path.Combine(root, "config.g"));
    manager.LoadArchive(config);
    archive.Write(Path.Combine(root, "fixture.g"));
    return manager;
}
