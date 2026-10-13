using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using AvaloniaEdit;
using EastwardLib.Assets;
using EastwardLib.Compilation;
using EastwardLib.MetaData;
using EastwardLib.Modding;
using EastwardLib.Utils;
using EastwardModTool;
using EastwardModTool.Core;
using EastwardModTool.Core.Documents;
using EastwardModTool.Views;

if (args.Length >= 2 && args[0] == "--game")
{
    InstalledGame(args[1], args.Length > 2 ? args[2] : null);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "EastwardModTool-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var manager = Fixture(root);
    using (var workspace = new GameWorkspace(manager, root))
    {
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
        var frame = sprite.GetImage("idle")!;
        var offset = 2 * 4;
        Check(frame.Width == 3 && frame.Height == 2 && frame.Pixels[offset] == 85 && frame.Pixels[offset + 1] == 170 && frame.Pixels[offset + 3] == 192, "Sprite preview lost sequence offsets or alpha compositing");
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
    var window = new MainWindow(["--game", root]);
    window.Show();
    Wait(window, () => window.GetVisualDescendants().OfType<ListBox>().First().ItemCount > 0);
    var list = window.GetVisualDescendants().OfType<ListBox>().First();
    list.SelectedItem = list.Items.OfType<AssetInfo>().Single(info => info.AssetName == "hello.txt");
    Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => box.Text == "original"));
    var editor = window.GetVisualDescendants().OfType<TextEditor>().Single(box => box.Text == "original");
    editor.Text = "UI edit";
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(window.Title!.Contains("Unsaved"), "UI editing did not reach document state");
        var screenshot = args.FirstOrDefault() ?? Path.Combine(root, "window.png");
        capture!.Save(screenshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine("RENDER " + screenshot);
    }
    var undo = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => editor.Text == "original");
    editor.Text = "keyboard edit";
    editor.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Z, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => editor.Text == "original");
    editor.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Y, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
    Wait(window, () => editor.Text == "keyboard edit");
    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => editor.Text == "original");
    var search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search assets…");
    search.Text = "texture.png";
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(list.ItemCount == 1, "UI asset search failed");
    }

    var indexButton = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Index"));
    indexButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "asset_index"));
    var metadataEditor = window.GetVisualDescendants().OfType<TextEditor>().Single(box => !box.IsReadOnly && box.Text?.Contains("objectFiles") == true);
    metadataEditor.Text += " ";
    using (var capture = window.CaptureRenderedFrame())
    {
        Check(window.Title!.Contains("Unsaved"), "Metadata UI did not edit its draft");
    }

    undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Wait(window, () => !window.Title!.Contains("Unsaved"));
    search.Text = "sprite.msprite";
    Wait(window, () => list.Items.OfType<AssetInfo>().Any(info => info.Type == "msprite"));
    list.SelectedItem = list.Items.OfType<AssetInfo>().Single(info => info.Type == "msprite");
    Wait(window, () => window.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null));
    window.Close();
    Console.WriteLine("PASS headless desktop layout, live text editing, undo and search");
}
finally
{
    Directory.Delete(root, true);
}

static void Wait(Window window, Func<bool> ready)
{
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!ready())
    {
        using var capture = window.CaptureRenderedFrame();
        if (DateTime.UtcNow > deadline)
        {
            throw new Exception("UI operation timed out");
        }

        Thread.Sleep(10);
    }
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
            var spriteInfo = workspace.Assets.Values.First(info => info.Type == "msprite" && info.AssetName.Contains("mika", StringComparison.OrdinalIgnoreCase));
            var sprite = (ResourceDocument)workspace.OpenAsset(spriteInfo.AssetName);
            var animation = sprite.Entries.First(entry => sprite.GetFrameCount(entry) > 0);
            Check(sprite.GetImage(animation) != null && sprite.GetImage(animation, sprite.GetFrameCount(animation) - 1) != null, "Installed sprite frames did not render");
            Console.WriteLine($"PASS installed workspace ({workspace.Assets.Count} entries), five mesh draft round trips and Mika frame rendering");
        }
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(["--game", directory]);
        window.Show();
        Wait(window, () => window.GetVisualDescendants().OfType<ListBox>().First().ItemCount > 0);
        var search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search assets…");
        search.Text = scriptName;
        var list = window.GetVisualDescendants().OfType<ListBox>().First();
        Wait(window, () => list.Items.OfType<AssetInfo>().Any(info => info.AssetName == scriptName));
        list.SelectedItem = list.Items.OfType<AssetInfo>().Single(info => info.AssetName == scriptName);
        Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => !box.IsReadOnly && box.Text == originalScript));
        if (screenshot != null)
        {
            using var capture = window.CaptureRenderedFrame();
            capture!.Save(screenshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        var metadataTimer = System.Diagnostics.Stopwatch.StartNew();
        var index = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Index"));
        index.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(window, () => window.GetVisualDescendants().OfType<TextEditor>().Any(box => !box.IsReadOnly && box.Text?.Contains("objectFiles") == true));
        using (var capture = window.CaptureRenderedFrame())
        {
            Check(capture != null, "Installed metadata editor did not render");
        }

        Console.WriteLine($"PASS installed global index editor rendered in {metadataTimer.Elapsed.TotalSeconds:F2}s");
        window.Close();
        Console.WriteLine("PASS desktop installed game indexing, search, source loading and rendering; game was not started");
    }
    finally
    {
        Directory.Delete(root, true);
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
          "hello.txt":{"type":"text","objectFiles":{"data":"fixture/text"}},
          "settings.json":{"type":"data_json","objectFiles":{"data":"fixture/settings"}},
          "event.sq":{"type":"sq_script","objectFiles":{"data":"fixture/sq"}},
          "fsm.graphml":{"type":"fsm_scheme","objectFiles":{"def":"fixture/fsm"}},
          "texture.png":{"type":"texture","objectFiles":{"pixmap":"fixture/image"}},
          "mesh.obj":{"type":"mesh","objectFiles":{"mesh":"fixture/mesh"}},
          "sprite.msprite":{"type":"msprite","objectFiles":{"def":"fixture/sprite"}},
          "sprite.msprite/atlas":{"type":"texture","objectFiles":{"pixmap":"fixture/atlas"}}
        }
        """);
    var manager = new AssetManager(assets, ScriptLibrary.Create("{\"source\":{}}"), TextureIndex.Create("{\"root\":\"1\",\"map\":{\"1\":{\"body\":{\"groups\":[]}}}}"));
    var archive = GArchive.Create("fixture");
    archive["fixture/text"] = "original"u8.ToArray();
    archive["fixture/settings"] = "{\"unknown\":false}"u8.ToArray();
    archive["fixture/sq"] = Encoding.UTF8.GetBytes(SQCompiler.Compile("!start\nsay hello", "event.sq").ToJsonString());
    archive["fixture/fsm"] = "return (function() nodelist={['start']={localName='start',type='state'};['next']={localName='next',type='state'};}; nodelist['start'].next={['$next']='next';[true]='next';}; return nodelist end) ()"u8.ToArray();
    archive["fixture/image"] = new RgbaImage(1, 1, [50, 60, 70, 128]).ToPng();
    archive["fixture/mesh/mesh"] = "<ASSIMP><Scene><Node><MeshRefs>0</MeshRefs></Node><MeshList><Mesh><Positions>0 0 0\n1 0 0\n0 1 0</Positions><Normals>0 0 1\n0 0 1\n0 0 1</Normals><FaceList><Face>0 1 2</Face></FaceList></Mesh></MeshList></Scene></ASSIMP>"u8.ToArray();
    archive["fixture/atlas"] = new RgbaImage(2, 1, [255, 0, 0, 128, 0, 255, 0, 128]).ToPng();
    archive["fixture/sprite"] = """
        {"atlases":{"a":"atlas"},"features":[],"anims":{"a":{"name":"idle","seq":[["f",0.1,2,-1]]}},"frames":{"f":{"parts":[["red",0,0],["green",0,0]],"meta":{}}},"modules":{"red":{"atlas":"a","rect":[0,0,1,1],"feature":0},"green":{"atlas":"a","rect":[1,0,1,1],"feature":0}}}
        """u8.ToArray();
    manager.LoadArchive(archive);
    var config = GArchive.Create("config");
    manager.SaveMetadata(config);
    config.Write(Path.Combine(root, "config.g"));
    manager.LoadArchive(config);
    archive.Write(Path.Combine(root, "fixture.g"));
    return manager;
}
