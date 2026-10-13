using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Core;

/// <summary>Editable source drafts and their game baseline, kept separately from distributable mods.</summary>
public static class WorkspaceProject
{
    public static void Save(GameWorkspace workspace, string path)
    {
        path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".eastward-project-" + Guid.NewGuid().ToString("N"));
        var sources = Path.Combine(Path.GetTempPath(), "EastwardModTool-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sources);
        try
        {
            var documents = new JsonArray();
            foreach (var document in workspace.Documents.Where(document => document.IsDirty))
            {
                var relative = "sources/" + documents.Count;
                var source = Path.Combine(sources, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                var kind = "text";
                if (document is TextDocument text)
                {
                    File.WriteAllText(source, text.Text);
                }
                else
                {
                    var asset = ((ResourceDocument)document).Current;
                    kind = "resource";
                    relative += asset switch
                    {
                        TexturePackAsset or TilesetPackAsset => ".psd",
                        HmgAsset => ".png",
                        PackageAsset or MSpriteAsset or LocalePackAsset => "/package",
                        _ => ".source"
                    };
                    source = Path.Combine(sources, relative);
                    document.ExportSource(source);
                }
                documents.Add(new JsonObject
                {
                    ["name"] = document.Name,
                    ["kind"] = kind,
                    ["source"] = relative,
                    ["originalHashes"] = JsonSerializer.SerializeToNode(workspace.GetOriginalHashes(document))
                });
            }
            var manifest = new JsonObject
            {
                ["format"] = "eastward-project",
                ["version"] = 1,
                ["gameDirectory"] = workspace.GameDirectory,
                ["overlayDirectory"] = workspace.OverlayDirectory,
                ["documents"] = documents
            };
            File.WriteAllText(Path.Combine(sources, "workspace.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ZipFile.CreateFromDirectory(sources, temporary, CompressionLevel.Optimal, false);
            File.Move(temporary, path, true);
            workspace.MarkSaved(path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            Directory.Delete(sources, true);
        }
    }

    public static GameWorkspace Open(string path, string? gameDirectory = null, string? decompilerPath = null)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
        {
            throw new InvalidDataException("Duplicate project entries");
        }
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName.TrimEnd('/'));
        }

        using var input = (archive.GetEntry("workspace.json") ?? throw new InvalidDataException("Missing project manifest")).Open();
        var manifest = JsonNode.Parse(input) ?? throw new InvalidDataException("Empty project manifest");
        if (manifest["format"]?.ToString() != "eastward-project" || manifest["version"]?.GetValue<int>() != 1)
        {
            throw new InvalidDataException("Unsupported project format");
        }
        var documents = manifest["documents"] as JsonArray ?? throw new InvalidDataException("Missing project documents");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!names.Add(document!["name"]!.GetValue<string>()))
            {
                throw new InvalidDataException("Duplicate project document");
            }

            ValidatePath(document["source"]!.GetValue<string>());
        }
        var workspace = GameWorkspace.Open(gameDirectory ?? manifest["gameDirectory"]!.GetValue<string>(), manifest["overlayDirectory"]?.GetValue<string>(), decompilerPath);
        var sources = Path.Combine(Path.GetTempPath(), "EastwardModTool-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sources);
        try
        {
            foreach (var record in documents)
            {
                var document = workspace.OpenAsset(record!["name"]!.GetValue<string>());
                var expected = record["originalHashes"]?.Deserialize<Dictionary<string, string?>>() ?? throw new InvalidDataException("Missing project baseline");
                var actual = workspace.GetOriginalHashes(document);
                if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
                {
                    throw new InvalidDataException("Project belongs to different game resources: " + document.Name);
                }
            }
            archive.ExtractToDirectory(sources);
            foreach (var record in documents)
            {
                var document = workspace.OpenAsset(record!["name"]!.GetValue<string>());
                var source = Path.Combine(sources, record["source"]!.GetValue<string>());
                switch (record["kind"]?.ToString())
                {
                    case "text" when document is TextDocument text:
                        text.Text = File.ReadAllText(source);
                        break;
                    case "resource" when document is ResourceDocument:
                        document.ImportSource(source);
                        break;
                    default:
                        throw new InvalidDataException("Project document type differs: " + document.Name);
                }
            }
            workspace.MarkSaved(path);
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
        finally
        {
            Directory.Delete(sources, true);
        }
    }

    private static void ValidatePath(string path)
    {
        if (path.Split('/').Any(part => part is "" or "." or ".." || part.IndexOfAny(['\\', ':', '\0']) >= 0))
        {
            throw new InvalidDataException("Invalid project entry path: " + path);
        }
    }
}
