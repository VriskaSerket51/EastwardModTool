using System.Buffers.Binary;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EastwardLib.Assets;
using EastwardLib.MetaData;
using EastwardLib.Utils;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Core;

internal sealed class LanguagePatchBuilder(GameWorkspace workspace)
{
    private readonly Dictionary<string, Asset> _changes = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Asset> Prepare(LanguagePatchOptions input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var options = input.Copy();
        ValidateLanguage(options.Language);
        ValidateLanguage(options.BaseLanguage);
        if (string.IsNullOrWhiteSpace(options.DisplayName) || options.DisplayName.Length > 128 || options.DisplayName.Any(char.IsControl))
        {
            throw new ArgumentException("Provide a display name of up to 128 characters");
        }
        if (options.DialogClass is not null and not "latin" and not "cn" and not "ja")
        {
            throw new ArgumentException("Dialogue class must be latin, cn or ja");
        }
        if (options.DialogLineSpacing is double line && (!double.IsFinite(line) || line < 0 || line > 256) || options.FirstRunButtonSpacing is double gap && (!double.IsFinite(gap) || gap < 0 || gap > 1024))
        {
            throw new ArgumentException("Invalid language layout spacing");
        }
        if (options.SourceFiles.Count > 1000)
        {
            throw new ArgumentException("Limit a language patch to 1000 source replacements");
        }
        foreach (var (name, source) in options.SourceFiles)
        {
            var document = workspace.OpenAsset(name);
            if (document.AssetType is not ("texture" or "texture_pack" or "deck_pack" or "deck_pack_raw" or "named_tileset_pack" or "msprite" or "font_ttf" or "font_bmfont"))
            {
                throw new ArgumentException("Select an image, sprite, deck or font source: " + name);
            }
            _changes.Add(name, document.ReadSource(source));
        }
        PrepareLocales(options);
        PrepareFont(options);
        if (options.ChapterGroup != null)
        {
            if (!Regex.IsMatch(options.ChapterGroup, "^[A-Za-z][A-Za-z0-9_]{0,31}$") || !workspace.Assets.ContainsKey("scene/design/ui/F_Overlay.scene/" + options.ChapterGroup + "_Prologue"))
            {
                throw new ArgumentException("Select an existing chapter title group");
            }
        }
        if (options.TitleTexture != null && (!workspace.Assets.TryGetValue(options.TitleTexture, out var texture) || texture.Type != "texture"))
        {
            throw new ArgumentException("Select an existing DLC title texture");
        }
        new LanguageImageBuilder(workspace, _changes).Prepare(options);
        foreach (var (name, source) in LanguageScriptPatcher.CreatePatches(workspace, options))
        {
            _changes.Add(name, new LuaAsset(System.Text.Encoding.UTF8.GetBytes(source)));
        }
        return _changes;
    }

    private void PrepareLocales(LanguagePatchOptions options)
    {
        var packs = workspace.Assets.Values.Where(info => info.Type == "locale_pack").ToDictionary(info => info.AssetName, info => (LocalePackAsset)workspace.OpenAsset(info.AssetName).Compile(), StringComparer.Ordinal);
        if (packs.Count == 0)
        {
            throw new InvalidDataException("No locale packs were found in this game");
        }
        var tables = packs.ToDictionary(pair => pair.Key, pair => pair.Value.Languages.ToDictionary(language => language.Key, language => language.Value.DeepClone(), StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (name, pack) in packs)
        {
            if (!tables[name].ContainsKey(options.Language))
            {
                if (!pack.Languages.TryGetValue(options.BaseLanguage, out var fallback))
                {
                    throw new InvalidDataException("Missing base language " + options.BaseLanguage + " in " + name);
                }
                tables[name].Add(options.Language, fallback.DeepClone());
            }
        }
        var items = new Dictionary<string, (string Pack, string Table)>(StringComparer.Ordinal);
        foreach (var (name, pack) in packs)
        {
            foreach (var item in pack.Config["items"]?.AsArray() ?? throw new InvalidDataException("Missing locale items: " + name))
            {
                var path = item?["path"]?.GetValue<string>() ?? throw new InvalidDataException("Missing locale item path");
                var table = item["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing locale table name");
                if (!items.TryAdd(path, (name, table)))
                {
                    throw new InvalidDataException("Ambiguous locale asset path: " + path);
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(options.LocaleDirectory))
        {
            var directory = Path.GetFullPath(options.LocaleDirectory);
            var configs = Directory.GetFiles(directory, "locale_pack.json", SearchOption.AllDirectories).ToList();
            var direct = Path.Combine(directory, "locale_pack.json");
            if (File.Exists(direct) && !configs.Contains(direct))
                configs.Add(direct);
            if (configs.Count == 0)
                throw new InvalidDataException("No locale_pack.json found in the selected folder");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in configs)
            {
                var config = JsonNode.Parse(File.ReadAllBytes(file)) ?? throw new InvalidDataException("Empty locale configuration");
                var candidates = packs.Where(pair => JsonNode.DeepEquals(pair.Value.Config["guid"], config["guid"]) && JsonNode.DeepEquals(pair.Value.Config["items"], config["items"])).Select(pair => pair.Key).ToArray();
                if (candidates.Length != 1 || !seen.Add(candidates[0]))
                    throw new InvalidDataException("Unknown or duplicate locale pack: " + file);
                var name = candidates[0];
                var imported = (LocalePackAsset)packs[name].CompileSource(Path.GetDirectoryName(file)!);
                if (!imported.Languages.TryGetValue(options.Language, out var importedTable))
                    throw new InvalidDataException("Missing target locale in " + file);
                foreach (var (table, messages) in importedTable.AsObject())
                {
                    if (messages is not JsonObject messageMap)
                        throw new InvalidDataException("Invalid locale table: " + table);
                    foreach (var (key, value) in messageMap)
                    {
                        ApplyMessage(name, table, key, value, packs, tables, options.Language);
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(options.TranslationFile))
        {
            var data = JsonNode.Parse(File.ReadAllBytes(options.TranslationFile)) ?? throw new InvalidDataException("Empty translation export");
            if (data["language"]?.ToString() != options.Language)
                throw new InvalidDataException("Translation export uses a different target language");
            var translations = data["translations"] as JsonArray ?? throw new InvalidDataException("Missing exported translations");
            var seen = new HashSet<(string Path, string Key)>();
            foreach (var entry in translations)
            {
                var path = entry?["path"]?.GetValue<string>() ?? throw new InvalidDataException("Missing translation path");
                var key = entry?["key"]?.GetValue<string>() ?? throw new InvalidDataException("Missing translation key");
                if (!seen.Add((path, key)))
                    throw new InvalidDataException("Duplicate translation: " + path + "/" + key);
                if (!items.TryGetValue(path, out var item))
                    throw new InvalidDataException("Unknown translation asset: " + path);
                ApplyMessage(item.Pack, item.Table, key, entry["value"], packs, tables, options.Language);
            }
        }
        foreach (var (name, pack) in packs)
        {
            _changes.Add(name, pack.WithLanguages(tables[name]));
        }
    }

    private static void ApplyMessage(string packName, string table, string key, JsonNode? value, IReadOnlyDictionary<string, LocalePackAsset> packs, Dictionary<string, Dictionary<string, JsonNode>> tables, string language)
    {
        if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out _))
            throw new InvalidDataException("Translation values must be strings: " + key);
        if (!packs[packName].Languages.Values.Any(data => data[table] is JsonObject messages && messages.ContainsKey(key)))
        {
            throw new InvalidDataException("Unknown locale message: " + table + "/" + key);
        }
        var target = tables[packName][language].AsObject();
        if (target[table] is not JsonObject)
            target[table] = new JsonObject();
        target[table]![key] = value.DeepClone();
    }

    private void PrepareFont(LanguagePatchOptions options)
    {
        if (options.StyleSheetAsset != null)
        {
            if (workspace.OpenAsset(options.StyleSheetAsset) is not TextDocument sheet || sheet.AssetType != "stylesheet")
                throw new ArgumentException("Select a stylesheet asset");
            var sheetData = ((JsonAsset)sheet.Compile()).Data;
            if (sheetData?["map"] is JsonObject map)
            {
                options.OriginalFontAssets = options.OriginalFontAssets.Concat(map.Select(pair => pair.Value?["body"]?["font"]).OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var name) ? name : "").Where(name => name.Length > 0)).Distinct(StringComparer.Ordinal).ToList();
            }
        }
        if (options.FontSource != null && options.FontAsset == null)
            throw new ArgumentException("Select a TTF font prototype before importing a font");
        if (options.FontAsset == null)
            return;
        if (!workspace.Assets.TryGetValue(options.FontAsset, out var font) || font.Type != "font_ttf")
            throw new ArgumentException("Select a TrueType/OpenType font asset");
        if (options.FontSource == null)
            return;
        var bytes = File.ReadAllBytes(options.FontSource);
        ValidateFont(bytes);
        var name = Path.ChangeExtension(options.FontAsset, null) + "." + options.Language + Path.GetExtension(options.FontAsset);
        var index = (TextDocument)workspace.OpenAsset("@asset_index");
        var data = ((JsonAsset)index.Compile()).Data!.AsObject();
        var clone = font.Data.DeepClone().AsObject();
        var originalPath = font.ObjectFiles["font"];
        var path = originalPath[..originalPath.IndexOf('/')] + "/eastward_modtool/locales/" + options.Language + "/" + name[(name.LastIndexOf('/') + 1)..];
        if (data.ContainsKey(name) && data[name]?["objectFiles"]?["font"]?.ToString() != path)
        {
            throw new InvalidDataException("The new font asset already exists: " + name);
        }
        clone["filePath"] = false;
        clone["objectFiles"] = new JsonObject { ["font"] = path };
        data[name] = clone;
        AssetIndex.Create(data.ToJsonString());
        _changes["@asset_index"] = new JsonAsset(data);
        var runtime = (RuntimeResourcesDocument)workspace.OpenAsset(RuntimeResourcesDocument.DocumentName);
        var resources = runtime.Resources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        resources[path] = bytes;
        _changes[RuntimeResourcesDocument.DocumentName] = new PackageAsset(resources.ToDictionary(pair => pair.Key, pair => (Asset)new BinaryAsset(pair.Value), StringComparer.Ordinal));
        options.FontAsset = name;
    }

    private static void ValidateFont(byte[] bytes)
    {
        if (bytes.Length < 12)
            throw new InvalidDataException("The font header is truncated");
        var offset = 0;
        if (bytes.AsSpan(0, 4).SequenceEqual("ttcf"u8))
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
            if (count == 0 || 12UL + 4UL * count > (ulong)bytes.LongLength)
                throw new InvalidDataException("Invalid font collection");
            var first = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
            if (first > bytes.Length - 12)
                throw new InvalidDataException("Invalid font collection offset");
            offset = (int)first;
        }
        if (!(bytes.AsSpan(offset, 4).SequenceEqual(new byte[] { 0, 1, 0, 0 }) || bytes.AsSpan(offset, 4).SequenceEqual("OTTO"u8)))
        {
            throw new InvalidDataException("Select a TrueType or OpenType font file");
        }
        var tables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
        if (tables == 0 || offset + 12L + tables * 16L > bytes.LongLength)
            throw new InvalidDataException("Invalid font table directory");
        var hasCharacters = false;
        for (var index = 0; index < tables; index++)
        {
            var entry = bytes.AsSpan(offset + 12 + index * 16, 16);
            var start = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            if ((ulong)start + size > (ulong)bytes.LongLength)
                throw new InvalidDataException("Font table extends beyond the file");
            hasCharacters |= entry[..4].SequenceEqual("cmap"u8) && size > 0;
        }
        if (!hasCharacters)
            throw new InvalidDataException("The font has no character map");
    }

    private static void ValidateLanguage(string language)
    {
        if (!Regex.IsMatch(language, "^[A-Za-z][A-Za-z0-9_-]{0,31}$") || language is "default" or "source" or "constructor" or "prototype")
        {
            throw new ArgumentException("Use a language ID such as ko, ar or pt-BR");
        }
    }
}
