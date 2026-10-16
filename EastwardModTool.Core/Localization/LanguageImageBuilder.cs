using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EastwardLib.Assets;
using EastwardLib.MetaData;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Core;

internal sealed class LanguageImageBuilder(GameWorkspace workspace, Dictionary<string, Asset> changes)
{
    public void Prepare(LanguagePatchOptions options)
    {
        if (options.LocalizedImages.Count == 0)
            return;
        if (options.LocalizedImages.Count > 1000)
            throw new ArgumentException("Limit a language patch to 1000 localized textures");
        var assets = Metadata("@asset_index");
        var textureData = Metadata("@texture_index");
        var textures = TextureIndex.Create(textureData.ToJsonString());
        var resources = changes.TryGetValue(RuntimeResourcesDocument.DocumentName, out var prepared)
            ? ((PackageAsset)prepared).Items.ToDictionary(pair => pair.Key, pair => pair.Value.Encode(), StringComparer.Ordinal)
            : ((RuntimeResourcesDocument)workspace.OpenAsset(RuntimeResourcesDocument.DocumentName)).Resources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var groupId = Id("group/" + options.Language);
        foreach (var (name, file) in options.LocalizedImages)
        {
            if (options.SourceFiles.ContainsKey(name))
                throw new ArgumentException("A texture cannot be both shared and language-specific: " + name);
            if (!workspace.Assets.TryGetValue(name, out var info) || info.Type != "texture" || !textures.Textures.TryGetValue(name, out var original))
            {
                throw new ArgumentException("Select an indexed texture for a language-specific image: " + name);
            }
            var source = workspace.OpenAsset(name);
            var image = (HmgAsset)source.ReadSource(file);
            var baseline = ((HmgAsset)source.Baseline).Image!;
            if (image.Image!.Width != baseline.Width || image.Image.Height != baseline.Height)
            {
                throw new InvalidDataException("Keep the original image canvas size for " + name + ": " + baseline.Width + " × " + baseline.Height);
            }
            var replacement = Path.ChangeExtension(name, null) + "." + options.Language + ".png";
            var archivePath = info.ObjectFiles.Values.FirstOrDefault(path => path.Contains('/')) ?? original.AtlasPath ?? throw new InvalidDataException("Missing texture archive path: " + name);
            var path = archivePath[..archivePath.IndexOf('/')] + "/eastward_modtool/locales/" + options.Language + "/textures/" + Id(name) + ".hmg";
            var node = info.Data.DeepClone().AsObject();
            node["filePath"] = false;
            node["objectFiles"] = new JsonObject { ["pixmap"] = path };
            if (assets.ContainsKey(replacement) && assets[replacement]?["objectFiles"]?["pixmap"]?.ToString() != path)
            {
                throw new InvalidDataException("Language image asset already exists: " + replacement);
            }
            assets[replacement] = node;
            resources[path] = image.Encode();
            options.TextureRemappings[name] = replacement;
            if (!textures.Groups.Any(group => group.Id == groupId))
            {
                var group = original.Group.DeepClone().AsObject();
                group["name"] = "EastwardModTool/" + options.Language;
                group["atlasMode"] = false;
                group["premultiplyAlpha"] = false;
                group["atlasCachePath"] = false;
                group["textures"] = new JsonArray();
                var originalGroup = textures.Groups.First(group => ReferenceEquals(group.Data, original.Group));
                var envelope = textures.Data["map"]![originalGroup.Id]!.DeepClone().AsObject();
                envelope["body"] = group;
                textures.AddGroup(groupId, envelope);
            }
            var entry = textures.Data["map"]![original.Id]!.DeepClone().AsObject();
            var body = entry["body"]!.AsObject();
            body["path"] = replacement;
            body["w"] = image.Image.Width;
            body["ow"] = image.Image.Width;
            body["h"] = image.Image.Height;
            body["oh"] = image.Image.Height;
            body["x"] = 0;
            body["y"] = 0;
            body["u0"] = 0;
            body["v0"] = 1;
            body["u1"] = 1;
            body["v1"] = 0;
            body["rotated"] = false;
            if (textures.Textures.TryGetValue(replacement, out var existing))
            {
                textures.Update(existing.Id, target => Replace(target, body));
            }
            else
            {
                textures.AddTexture(groupId, Id("texture/" + options.Language + "/" + name), entry);
            }
        }
        if (options.TitleTexture != null && options.TextureRemappings.TryGetValue(options.TitleTexture, out var title))
            options.TitleTexture = title;
        if (options.ChapterGroup != null)
        {
            const string prefix = "scene/design/ui/F_Overlay.scene/";
            var group = options.ChapterGroup;
            var replacementGroup = "EW_" + options.Language.Replace('-', '_');
            var prototypes = workspace.Assets.Values.Where(info => info.Type == "proto" && info.AssetName.StartsWith(prefix + group + "_", StringComparison.Ordinal)).ToArray();
            if (prototypes.Length == 0)
                throw new InvalidDataException("No chapter prototypes found for " + group);
            foreach (var prototype in prototypes)
            {
                var name = prefix + replacementGroup + prototype.AssetName[(prefix + group).Length..];
                var data = ((JsonAsset)workspace.OpenAsset(prototype.AssetName).Compile()).Data!.DeepClone();
                Rewrite(data, options.TextureRemappings);
                var node = prototype.Data.DeepClone().AsObject();
                node["filePath"] = false;
                var files = new JsonObject();
                foreach (var role in new[] { "def", "packed_def" })
                {
                    if (!prototype.ObjectFiles.TryGetValue(role, out var originalPath))
                        continue;
                    var path = originalPath[..originalPath.IndexOf('/')] + "/eastward_modtool/locales/" + options.Language + "/protos/" + Id(name) + (role == "def" ? ".json" : ".packed");
                    files[role] = path;
                    resources[path] = new JsonAsset(data.DeepClone(), role == "packed_def").Encode();
                }
                if (files.Count == 0)
                    throw new InvalidDataException("Missing chapter proto definition: " + prototype.AssetName);
                if (assets.ContainsKey(name) && !JsonNode.DeepEquals(assets[name]?["objectFiles"], files))
                    throw new InvalidDataException("Chapter proto already exists: " + name);
                node["objectFiles"] = files;
                assets[name] = node;
            }
            options.ChapterGroup = replacementGroup;
        }
        AssetIndex.Create(assets.ToJsonString());
        changes["@asset_index"] = new JsonAsset(assets);
        changes["@texture_index"] = new JsonAsset(textures.Data);
        changes[RuntimeResourcesDocument.DocumentName] = new PackageAsset(resources.ToDictionary(pair => pair.Key, pair => (Asset)new BinaryAsset(pair.Value), StringComparer.Ordinal));
    }

    private JsonObject Metadata(string name) => ((JsonAsset)(changes.GetValueOrDefault(name) ?? workspace.OpenAsset(name).Compile())).Data!.DeepClone().AsObject();

    private static string Id(string name) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("EastwardModTool/" + name))).ToLowerInvariant()[..32];

    private static void Replace(JsonObject target, JsonObject source)
    {
        target.Clear();
        foreach (var (key, value) in source)
            target[key] = value?.DeepClone();
    }

    private static void Rewrite(JsonNode node, IReadOnlyDictionary<string, string> replacements)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToArray())
            {
                if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && replacements.TryGetValue(text, out var replacement))
                    obj[key] = replacement;
                else if (value != null)
                    Rewrite(value, replacements);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue scalar && scalar.TryGetValue<string>(out var text) && replacements.TryGetValue(text, out var replacement))
                    array[index] = replacement;
                else if (array[index] != null)
                    Rewrite(array[index]!, replacements);
            }
        }
    }
}
