using System.Text;
using System.Text.Json.Nodes;
using EastwardLib.Assets;
using EastwardLib.Compilation;
using EastwardLib.Modding;
using EastwardLib.MetaData;
using EastwardLib.Utils;
using EastwardModTool.Core;
using EastwardModTool.Core.Documents;

PoEmptyValues();
if (args.Length < 2)
{
    Console.WriteLine("PO empty-value tests passed. Pass GAME_DIRECTORY OUTPUT_DIRECTORY for installed-resource tests.");
    return;
}

var game = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Require(!IsWithin(game, output) && !IsWithin(output, game), "Test outputs must be separate from the installed game");
Directory.CreateDirectory(output);
var archives = Directory.EnumerateFiles(game, "*.g", SearchOption.AllDirectories).ToDictionary(path => path, path => (new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)));
using var workspace = GameWorkspace.Open(game, decompilerPath: args.ElementAtOrDefault(2));
var assetName = workspace.Assets.First(pair => pair.Value.Type == "locale_pack" && pair.Key.EndsWith("UI.locale_pack", StringComparison.Ordinal)).Key;
var document = (ResourceDocument)workspace.OpenAsset(assetName);
var original = (LocalePackAsset)document.Baseline;
var languages = original.Languages.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone(), StringComparer.Ordinal);
const string language = "x-language-test";
var messages = languages["en"].DeepClone();
var item = messages.AsObject().First(pair => pair.Value is JsonObject entries && entries.Count >= 2);
var keys = item.Value!.AsObject().Select(pair => pair.Key).Take(2).ToArray();
const string translated = "한글 日本語 مرحبا \"quoted\"\r\n\t\\path";
item.Value[keys[0]] = translated;
item.Value[keys[1]] = "";
languages.Add(language, messages);
var edited = original.WithLanguages(languages);
edited.Config["language_test_unknown"] = new JsonObject { ["preserved"] = true };
Require(!original.Config.AsObject().ContainsKey("language_test_unknown"), "WithLanguages must not mutate the original config");
Require(original.Languages.All(pair => JsonNode.DeepEquals(pair.Value, edited.Languages[pair.Key])), "Existing languages must remain unchanged");
languages[language][item.Key]![keys[0]] = "must not affect clone";
Require(edited.Languages[language][item.Key]![keys[0]]!.GetValue<string>() == translated, "WithLanguages must clone input tables");
document.ImportAsset(edited);
var projectPath = Path.Combine(output, "locale-roundtrip.ewproj");
WorkspaceProject.Save(workspace, projectPath);
using var reopened = WorkspaceProject.Open(projectPath, game, args.ElementAtOrDefault(2));
var restored = (LocalePackAsset)((ResourceDocument)reopened.OpenAsset(assetName)).Current;
Require(JsonNode.DeepEquals(edited.Config, restored.Config), "Project round-trip must preserve unknown locale config");
Require(restored.Languages[language][item.Key]![keys[0]]!.GetValue<string>() == translated, "Project round-trip must preserve Unicode, CR, LF, tabs and slashes");
Require(restored.Languages[language][item.Key]![keys[1]]!.GetValue<string>() == "", "Project round-trip must preserve intentionally empty translations");
Require(original.Languages.All(pair => JsonNode.DeepEquals(pair.Value, restored.Languages[pair.Key])), "Project round-trip must preserve all existing languages");
var package = reopened.BuildMod("language-roundtrip");
var resource = package.ResourcePaths.Single(path => path.EndsWith("/" + language, StringComparison.Ordinal));
var lua = LuaTable.Parse(Encoding.UTF8.GetString(package.GetResource(resource)))!;
Require(JsonNode.DeepEquals(lua, restored.Languages[language]), "Compiled Lua table must match the new language exactly");
var packagePath = Path.Combine(output, "locale-roundtrip.ewmod");
package.SaveTo(packagePath);
Require(ModPackage.Read(packagePath).GetResource(resource).AsSpan().SequenceEqual(package.GetResource(resource)), "Portable mod must retain language resource bytes");
LanguageWizard(game, output, args.ElementAtOrDefault(2));
Require(archives.All(pair => new FileInfo(pair.Key).Length == pair.Value.Length && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Item2), "Tests must not write installed archives");
Console.WriteLine($"Installed locale source/project/mod round-trip passed: {original.Languages.Count} original languages preserved, {language} compiled.");

static void LanguageWizard(string game, string output, string? decompiler)
{
    using var wizard = GameWorkspace.Open(game, decompilerPath: decompiler);
    var packName = wizard.Assets.First(pair => pair.Value.Type == "locale_pack" && pair.Key.EndsWith("UI.locale_pack", StringComparison.Ordinal)).Key;
    var original = (LocalePackAsset)wizard.OpenAsset(packName).Baseline;
    var item = original.Config["items"]!.AsArray().First(node => original.Languages["en"][node!["name"]!.GetValue<string>()] is JsonObject table && table.Count >= 2)!;
    var tableName = item["name"]!.GetValue<string>();
    var assetPath = item["path"]!.GetValue<string>();
    var keys = original.Languages["en"][tableName]!.AsObject().Select(pair => pair.Key).Take(2).ToArray();
    const string language = "x-wizard-test";
    const string value = "한글 日本語 مرحبا \"quoted\"\r\n\t\\path";
    var translation = new JsonObject
    {
        ["language"] = language,
        ["translations"] = new JsonArray(
            new JsonObject { ["path"] = assetPath, ["key"] = keys[0], ["value"] = value },
            new JsonObject { ["path"] = assetPath, ["key"] = keys[1], ["value"] = "" })
    };
    var jsonPath = Path.Combine(output, "wizard-translations.json");
    File.WriteAllText(jsonPath, translation.ToJsonString());
    var texture = wizard.Assets.Where(pair => pair.Value.Type == "texture").Select(pair => wizard.OpenAsset(pair.Key)).OfType<ResourceDocument>().First(document => document.Baseline is HmgAsset { Image: not null });
    var image = ((HmgAsset)texture.Baseline).Image!;
    var pixels = image.Pixels.ToArray();
    var pixel = Array.FindIndex(pixels, value => value > 0);
    pixels[pixel >= 0 ? pixel : 0] ^= 1;
    pixels[3] = 255;
    var imagePath = Path.Combine(output, "wizard-image.png");
    new HmgAsset(new RgbaImage(image.Width, image.Height, pixels)).SaveTo(imagePath);
    var fontName = wizard.Assets.First(pair => pair.Value.Type == "font_ttf").Key;
    var font = wizard.OpenAsset(fontName).Baseline.Encode();
    var fontPath = Path.Combine(output, "wizard-font.ttf");
    File.WriteAllBytes(fontPath, font);
    const string chapterTexture = "texture/ui/ChapterTitle/en/T0.png";
    var chapter = (HmgAsset)wizard.OpenAsset(chapterTexture).Baseline;
    var chapterPixels = chapter.Image!.Pixels.ToArray();
    chapterPixels[0] ^= 1;
    chapterPixels[3] = 255;
    var chapterSource = Path.Combine(output, "wizard-chapter.png");
    new HmgAsset(new RgbaImage(chapter.Image.Width, chapter.Image.Height, chapterPixels)).SaveTo(chapterSource);
    var options = new LanguagePatchOptions
    {
        Language = language,
        DisplayName = "Language test / 언어",
        BaseLanguage = "en",
        TranslationFile = jsonPath,
        FontAsset = fontName,
        FontSource = fontPath,
        SourceFiles = new Dictionary<string, string> { [texture.Name] = imagePath },
        LocalizedImages = new Dictionary<string, string> { [chapterTexture] = chapterSource },
        ChapterGroup = "EN"
    };
    var documents = wizard.CreateLanguagePatch(options);
    Require(documents.Count > 17, "Wizard must include runtime scripts alongside locale packs");
    Require(options.FontAsset == fontName, "Wizard must not mutate caller options");
    foreach (var script in documents.Where(document => document.Baseline is LuaAsset))
    {
        var text = Encoding.UTF8.GetString(((LuaAsset)script.Compile()).Encode());
        Require(OriginalPayload(text).AsSpan().SequenceEqual(((LuaAsset)script.Baseline).Encode()), "Generated script must preserve the exact original Lua bytes: " + script.Name);
    }
    var secondPatches = LanguageScriptPatcher.CreatePatches(wizard, new LanguagePatchOptions { Language = "x-second", DisplayName = "Second" });
    foreach (var (name, text) in secondPatches)
    {
        Require(OriginalPayload(text).AsSpan().SequenceEqual(((LuaAsset)wizard.OpenAsset(name).Baseline).Encode()), "Adding another language must not nest or replace the original payload");
        var header = text.Split('\n')[1]["-- options:".Length..];
        var configurations = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header)))!.AsArray();
        Require(configurations.Count == 2 && configurations.Any(value => value!["language"]!.ToString() == language), "Adding a language must retain the first language's runtime settings");
    }
    foreach (var document in documents.Where(document => document.Baseline is LocalePackAsset))
    {
        var before = (LocalePackAsset)document.Baseline;
        var after = (LocalePackAsset)document.Compile();
        Require(JsonNode.DeepEquals(before.Config, after.Config), "Wizard must preserve each original pack configuration");
        Require(before.Languages.All(pair => JsonNode.DeepEquals(pair.Value, after.Languages[pair.Key])), "Wizard must preserve every original language");
        Require(after.Languages.ContainsKey(language), "Wizard must register the target in each locale pack");
    }
    var changed = (LocalePackAsset)wizard.OpenAsset(packName).Compile();
    Require(changed.Languages[language][tableName]![keys[0]]!.GetValue<string>() == value, "Wizard must import exact Unicode and whitespace");
    Require(changed.Languages[language][tableName]![keys[1]]!.GetValue<string>() == "", "Wizard must import intentional empty translations");
    var project = Path.Combine(output, "language-wizard.ewproj");
    WorkspaceProject.Save(wizard, project);
    using var reopened = WorkspaceProject.Open(project, game, decompiler);
    var restored = (LocalePackAsset)reopened.OpenAsset(packName).Compile();
    Require(JsonNode.DeepEquals(restored.Languages[language], changed.Languages[language]), "Wizard project must preserve all translation values");
    var resources = (RuntimeResourcesDocument)reopened.OpenAsset(RuntimeResourcesDocument.DocumentName);
    Require(resources.Resources.Values.Any(bytes => bytes.AsSpan().SequenceEqual(font)), "Wizard project must preserve imported font bytes");
    var imageEntry = resources.Entries.First(name => name.EndsWith(".hmg", StringComparison.Ordinal));
    Require(resources.OpenItem(imageEntry) is ResourceDocument imagePreview && imagePreview.Current is HmgAsset && !imagePreview.CanEdit, "Generated images must be reviewable without editing the runtime bytes");
    var fontEntry = resources.Entries.First(name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
    Require(resources.OpenItem(fontEntry).AssetType == "font_ttf", "Generated fonts must use the existing font preview");
    var protoEntry = resources.Entries.First(name => name.EndsWith(".json", StringComparison.Ordinal));
    Require(resources.OpenItem(protoEntry) is TextDocument protoPreview && !protoPreview.CanEdit, "Generated chapter definitions must expose a readable source preview");
    var mod = reopened.BuildMod("Language test");
    Require(reopened.ValidateMod(mod).Count == 0, "New language mod must validate against the unchanged installed game");
    var fontResource = mod.ResourcePaths.First(path => path.Contains("/eastward_modtool/locales/" + language + "/", StringComparison.Ordinal));
    Require(mod.GetResource(fontResource).AsSpan().SequenceEqual(font), "Compiled mod must contain exact font bytes");
    Require(mod.ResourcePaths.Any(path => path.EndsWith("/" + language, StringComparison.Ordinal)), "Compiled mod must contain target locale tables");
    foreach (var info in reopened.Assets.Values.Where(info => info.Type == "locale_pack"))
    {
        var before = (LocalePackAsset)reopened.OpenAsset(info.AssetName).Baseline;
        Require(!mod.ResourcePaths.Contains(info.ObjectFiles["config"]), "Unchanged locale config bytes must be retained");
        foreach (var oldLanguage in before.Languages.Keys)
        {
            Require(!mod.ResourcePaths.Contains(info.ObjectFiles["data"] + "/" + oldLanguage), "Unchanged original locale resource bytes must not be rewritten");
        }
    }
    var imageRole = reopened.Assets[texture.Name].ObjectFiles.Values.First();
    Require(mod.ResourcePaths.Contains(imageRole), "Compiled mod must include image source replacement");
    mod.SaveTo(Path.Combine(output, "language-wizard.ewmod"));
    var assets = AssetIndex.Create(((JsonAsset)reopened.OpenAsset("@asset_index").Compile()).Data!.ToJsonString());
    var textures = TextureIndex.Create(((JsonAsset)reopened.OpenAsset("@texture_index").Compile()).Data!.ToJsonString());
    var scripts = ScriptLibrary.Create(((JsonAsset)reopened.OpenAsset("@script_library").Compile()).Data!.ToJsonString());
    var overlay = new AssetManager(assets, scripts, textures);
    foreach (var file in Directory.EnumerateFiles(reopened.GameDirectory, "*.g", SearchOption.AllDirectories))
        overlay.RegisterArchive(file);
    if (reopened.OverlayDirectory is string dlc)
        foreach (var file in Directory.EnumerateFiles(dlc, "*.g", SearchOption.AllDirectories))
            overlay.RegisterArchive(file);
    foreach (var group in mod.ResourcePaths.GroupBy(path => path[..path.IndexOf('/')]))
    {
        var archive = GArchive.Create(group.Key);
        foreach (var path in group)
            archive[path] = mod.GetResource(path);
        overlay.LoadArchive(archive);
    }
    var localizedTexture = Path.ChangeExtension(chapterTexture, null) + "." + language + ".png";
    var restoredImage = (HmgAsset)overlay.LoadAsset(localizedTexture)!;
    Require(restoredImage.Image!.Pixels.AsSpan().SequenceEqual(chapterPixels), "Localized image must reload with exact straight-alpha pixels");
    Require(((HmgAsset)overlay.LoadAsset(chapterTexture)!).Image!.Pixels.AsSpan().SequenceEqual(chapter.Image.Pixels), "Localized image must not replace the original language image");
    var textureInfo = textures.Textures[localizedTexture];
    Require(!textureInfo.AtlasMode && !textureInfo.PremultiplyAlpha, "Localized standalone image must not inherit atlas or alpha conversion");
    Require(textureInfo.Data["u0"]!.GetValue<int>() == 0 && textureInfo.Data["u1"]!.GetValue<int>() == 1 && textureInfo.Data["v0"]!.GetValue<int>() == 1 && textureInfo.Data["v1"]!.GetValue<int>() == 0, "Localized image must have full standalone UVs");
    var chapterGroup = "scene/design/ui/F_Overlay.scene/EW_" + language.Replace('-', '_') + "_";
    var prototypes = assets.Values.Where(info => info.Type == "proto" && info.AssetName.StartsWith(chapterGroup, StringComparison.Ordinal)).ToArray();
    Require(prototypes.Length > 0, "Localized chapter prototypes must be indexed");
    Require(prototypes.Any(info => ((JsonAsset)overlay.LoadAsset(info.AssetName)!).Data!.ToJsonString().Contains(localizedTexture, StringComparison.Ordinal)), "Chapter prototypes must refer to the localized image");
    Console.WriteLine($"Localized image overlay passed: {prototypes.Length} chapter prototypes, straight alpha/full UVs, original image unchanged.");

    using var failed = GameWorkspace.Open(game, decompilerPath: decompiler);
    translation["translations"]![0]!["key"] = "missing-message-id";
    var badJson = Path.Combine(output, "invalid-translations.json");
    File.WriteAllText(badJson, translation.ToJsonString());
    try
    {
        failed.CreateLanguagePatch(new LanguagePatchOptions { Language = "x-invalid", DisplayName = "Invalid", TranslationFile = badJson, FontAsset = fontName, FontSource = fontPath });
        throw new InvalidOperationException("Invalid translation must be rejected");
    }
    catch (InvalidDataException) { }
    Require(!failed.HasChanges && !failed.IsModified, "Rejected wizard input must not change any draft documents");
    var badFont = Path.Combine(output, "invalid-font.ttf");
    File.WriteAllBytes(badFont, "invalid-font-header"u8.ToArray());
    try
    {
        failed.CreateLanguagePatch(new LanguagePatchOptions { Language = "x-invalid", DisplayName = "Invalid font", FontAsset = fontName, FontSource = badFont, SourceFiles = new Dictionary<string, string> { [texture.Name] = imagePath } });
        throw new InvalidOperationException("Invalid font must be rejected");
    }
    catch (InvalidDataException) { }
    Require(!failed.HasChanges && !failed.IsModified, "Invalid font must leave staged image and locale documents unchanged");
    var fakeFont = new byte[12];
    fakeFont[1] = 1;
    File.WriteAllBytes(badFont, fakeFont);
    try
    {
        failed.CreateLanguagePatch(new LanguagePatchOptions { Language = "x-invalid", DisplayName = "Incomplete font", FontAsset = fontName, FontSource = badFont });
        throw new InvalidOperationException("A valid signature without font tables must be rejected");
    }
    catch (InvalidDataException) { }
    Require(!failed.HasChanges && !failed.IsModified, "Incomplete font must leave all documents unchanged");


    Console.WriteLine($"Language wizard round-trip passed: {documents.Count} documents, {mod.ResourcePaths.Count} compiled resources, image/font replacements and failed-input atomicity.");
}

static byte[] OriginalPayload(string source)
{
    const string prefix = "local __ew_original_source = ";
    var literal = source.Split('\n')[2][prefix.Length..];
    var escapes = System.Text.RegularExpressions.Regex.Matches(literal, @"\\(\d{3})");
    Require(escapes.Count * 4 + 2 == literal.Length, "Original Lua payload must contain only byte escapes");
    return escapes.Select(match => byte.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}

static void PoEmptyValues()
{
    var po = "msgid \"untranslated\"\nmsgstr \"\"\n\n#, eastward-empty\nmsgid \"intentional\"\nmsgstr \"\"\n\n#, fuzzy, eastward-empty\nmsgid \"fuzzy\"\nmsgstr \"\"\n\nmsgid \"crlf\"\nmsgstr \"one\\r\\ntwo\"\n";
    var data = LocaleCompiler.ReadPo(po);
    Require(!data.ContainsKey("untranslated"), "Ordinary empty PO strings remain untranslated");
    Require(data["intentional"]!.GetValue<string>() == "", "Explicit empty strings must be retained");
    Require(!data.ContainsKey("fuzzy"), "Fuzzy entries must remain untranslated even with empty markers");
    Require(data["crlf"]!.GetValue<string>() == "one\r\ntwo", "Embedded CR/LF must be preserved");
    try
    {
        LocaleCompiler.ReadPo("msgid \"duplicate\"\nmsgstr \"\"\n\nmsgid \"duplicate\"\nmsgstr \"new\"");
        throw new InvalidOperationException("Duplicate untranslated IDs must be rejected");
    }
    catch (InvalidDataException) { }

}
static bool IsWithin(string parent, string child)
{
    var relative = Path.GetRelativePath(parent, child);
    return relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
