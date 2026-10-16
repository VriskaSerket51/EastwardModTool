using System.ComponentModel;
using System.Security.Cryptography;
using EastwardLib.Assets;
using EastwardLib.Compilation;
using EastwardLib.MetaData;
using EastwardLib.Modding;
using EastwardLib.Utils;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Core;

public sealed class GameWorkspace : IDisposable
{
    private readonly AssetManager _manager;
    private readonly Dictionary<string, AssetDocument> _documents = new(StringComparer.Ordinal);
    private readonly NativeLuaDecompiler _decompiler;
    private Dictionary<string, object> _savedStates = new(StringComparer.Ordinal);
    public string GameDirectory
    {
        get;
    }
    public string? OverlayDirectory
    {
        get; private set;
    }
    public string? ProjectPath
    {
        get; private set;
    }
    public bool IsModified => _documents.Values.Count(document => document.IsDirty) != _savedStates.Count || _documents.Values.Where(document => document.IsDirty).Any(document => !_savedStates.TryGetValue(document.Name, out var state) || !Equals(state, document.CurrentState));
    public IReadOnlyDictionary<string, AssetInfo> Assets => _manager.Assets;
    public IReadOnlyCollection<AssetDocument> Documents => _documents.Values;
    public bool HasChanges => _documents.Values.Any(document => document.IsDirty);
    public event EventHandler? Changed;

    public GameWorkspace(AssetManager manager, string gameDirectory, string? decompilerPath = null)
    {
        _manager = manager;
        GameDirectory = Path.GetFullPath(gameDirectory);
        _decompiler = new NativeLuaDecompiler(decompilerPath);
        _manager.LuaDecompiler ??= _decompiler.Decompile;
    }

    public static GameWorkspace Open(string directory, string? overlay = null, string? decompilerPath = null)
    {
        directory = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(directory, "config.g")))
        {
            directory = Path.Combine(directory, "content", "game");
        }
        var manager = AssetManager.Create(Path.Combine(directory, "config.g"));
        manager.RootDirectory = directory;
        foreach (var file in Directory.EnumerateFiles(directory, "*.g", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            manager.RegisterArchive(file);
        }

        overlay ??= Path.Combine(Path.GetFullPath(Path.Combine(directory, "../..")), "content_dlc");
        if (Directory.Exists(overlay))
        {
            foreach (var file in Directory.EnumerateFiles(overlay, "*.g", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                manager.RegisterArchive(file);
            }
        }
        return new GameWorkspace(manager, directory, decompilerPath) { OverlayDirectory = Directory.Exists(overlay) ? Path.GetFullPath(overlay) : null };
    }

    internal Asset LoadOriginalAsset(string name) => _manager.LoadAsset(name) ?? throw new InvalidDataException("Missing asset: " + name);

    public AssetDocument OpenAsset(string name)
    {
        if (_documents.TryGetValue(name, out var document))
        {
            return document;
        }

        if (name == RuntimeResourcesDocument.DocumentName)
        {
            document = new RuntimeResourcesDocument();
            document.PropertyChanged += DocumentChanged;
            _documents.Add(name, document);
            return document;
        }
        if (name.StartsWith('@'))
        {
            var data = name switch
            {
                "@asset_index" => _manager.Index.Data,
                "@script_library" => _manager.Scripts.Data,
                "@texture_index" => _manager.Textures.Data,
                _ => throw new ArgumentException("Unknown metadata document", nameof(name))
            };
            document = new MetadataDocument(name, new JsonAsset(data.DeepClone())) { AssetType = "metadata" };
            document.PropertyChanged += DocumentChanged;
            _documents.Add(name, document);
            return document;
        }
        var info = _manager.Assets[name];
        Asset? original = null;
        try
        {
            original = _manager.LoadAsset(name) ?? throw new InvalidDataException("Missing asset: " + name);
            var editable = AssetCompiler.Supports(info.Type);
            document = original switch
            {
                MSpriteAsset or DeckAsset or SpriteSequenceAsset or LegacyDeckAsset or LegacyDeckPackAsset or NamedTilesetAsset or BitmapFontAsset or MultiTextureAsset => new ResourceDocument(name, original, editable),
                LuaAsset lua when lua.IsBytecode && _manager.LuaDecompiler == null => new ResourceDocument(name, original, editable),
                TextAsset or JsonAsset or LuaAsset => new TextDocument(name, original, editable),
                _ => new ResourceDocument(name, original, editable)
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            document = original is LuaAsset lua ? new ResourceDocument(name, lua, AssetCompiler.Supports(info.Type)) { PreviewWarning = error.Message } : new UnavailableDocument(name, error.Message);
        }
        document.AssetType = info.Type;
        document.PropertyChanged += DocumentChanged;
        _documents.Add(name, document);
        return document;
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AssetDocument.IsDirty))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void MarkSaved(string path)
    {
        ProjectPath = Path.GetFullPath(path);
        _savedStates = _documents.Values.Where(document => document.IsDirty).ToDictionary(document => document.Name, document => document.CurrentState, StringComparer.Ordinal);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal Dictionary<string, string?> GetOriginalHashes(AssetDocument document)
    {
        var resources = document is MetadataDocument or RuntimeResourcesDocument ? new Dictionary<string, byte[]>() : new AssetCompiler(_manager).Compile(document.Name, document.Baseline);
        var hashes = resources.Keys.ToDictionary(path => path, path =>
        {
            var bytes = _manager.LoadFile(path);
            return bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }, StringComparer.Ordinal);
        hashes["@assetIndex"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_manager.Index.ToJson(false)))).ToLowerInvariant();
        hashes["@scriptLibrary"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_manager.Scripts.ToJson(false)))).ToLowerInvariant();
        hashes["@textureIndex"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_manager.Textures.ToJson(false)))).ToLowerInvariant();
        return hashes;
    }

    public IReadOnlyList<AssetDocument> CreateLanguagePatch(LanguagePatchOptions options)
    {
        var changes = new LanguagePatchBuilder(this).Prepare(options);
        // Compile the entire proposed patch before committing any document changes.
        BuildMod(options.DisplayName, changes);
        var documents = new List<AssetDocument>();
        foreach (var (name, asset) in changes)
        {
            var document = OpenAsset(name);
            if (document is RuntimeResourcesDocument runtime)
            {
                runtime.ReplaceResources(((PackageAsset)asset).Items.ToDictionary(pair => pair.Key, pair => pair.Value.Encode(), StringComparer.Ordinal));
            }
            else
            {
                document.ImportAsset(asset);
            }
            documents.Add(document);
        }
        return documents;
    }

    public ModPackage BuildMod(string name) => BuildMod(name, null);

    private ModPackage BuildMod(string name, IReadOnlyDictionary<string, Asset>? proposed)
    {
        var edited = _documents.Values.Where(document => document.IsDirty && document is not MetadataDocument and not RuntimeResourcesDocument).ToDictionary(document => document.Name, document => document.Compile(), StringComparer.Ordinal);
        var metadata = _documents.Values.OfType<MetadataDocument>().Where(document => document.IsDirty).ToDictionary(document => document.Name, document => ((JsonAsset)document.Compile()).Data!.ToJsonString(), StringComparer.Ordinal);
        var added = _documents.Values.OfType<RuntimeResourcesDocument>().Where(document => document.IsDirty).SelectMany(document => document.Resources).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (proposed != null)
        {
            foreach (var (assetName, asset) in proposed)
            {
                if (assetName == RuntimeResourcesDocument.DocumentName)
                {
                    added = ((PackageAsset)asset).Items.ToDictionary(pair => pair.Key, pair => pair.Value.Encode(), StringComparer.Ordinal);
                }
                else if (assetName.StartsWith('@'))
                {
                    metadata[assetName] = ((JsonAsset)asset).Data!.ToJsonString();
                }
                else
                {
                    edited[assetName] = asset;
                }
            }
        }
        Dictionary<string, byte[]> Merge(IReadOnlyDictionary<string, byte[]> compiled)
        {
            var resources = new Dictionary<string, byte[]>(compiled, StringComparer.Ordinal);
            foreach (var (path, bytes) in added)
            {
                if (resources.TryGetValue(path, out var previous) && !previous.AsSpan().SequenceEqual(bytes))
                {
                    throw new InvalidDataException("Conflicting runtime resource: " + path);
                }
                resources[path] = bytes;
            }
            return resources;
        }
        if (metadata.Count == 0)
        {
            return ModPackage.Create(name, _manager, Merge(new AssetCompiler(_manager).Compile(edited)));
        }

        var session = _manager.CopyForEditing();
        try
        {
            session.EditMetadata((assets, scripts, textures) =>
            {
                if (metadata.TryGetValue("@asset_index", out var assetJson))
                {
                    Replace(assets.Data, AssetIndex.Create(assetJson).Data);
                }

                if (metadata.TryGetValue("@script_library", out var scriptJson))
                {
                    Replace(scripts.Data, ScriptLibrary.Create(scriptJson).Data);
                }

                if (metadata.TryGetValue("@texture_index", out var textureJson))
                {
                    Replace(textures.Data, TextureIndex.Create(textureJson).Data);
                }
            });
            var compiler = new AssetCompiler(session);
            var resources = Merge(compiler.Compile(edited));
            ValidateMetadataResources(session, resources);
            foreach (var (path, bytes) in compiler.CompileMetadata())
            {
                resources.Add(path, bytes);
            }

            return ModPackage.Create(name, _manager, resources);
        }
        finally
        {
            session.UnloadArchives();
        }
    }

    private void ValidateMetadataResources(AssetManager session, IReadOnlyDictionary<string, byte[]> resources)
    {
        bool Available(string path) => resources.ContainsKey(path) || resources.Keys.Any(resource => resource.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal)) || session.FileExists(path) || session.GetFiles(path).Count > 0;
        foreach (var info in session.Assets.Values)
        {
            _manager.Assets.TryGetValue(info.AssetName, out var original);
            foreach (var (role, path) in info.ObjectFiles)
            {
                if (original?.ObjectFiles.GetValueOrDefault(role) != path && !Available(path))
                {
                    throw new InvalidDataException("Metadata references a missing resource: " + path);
                }
            }
        }
        foreach (var (name, path) in session.Scripts)
        {
            if (_manager.Scripts.GetValueOrDefault(name) != path && !Available(path))
            {
                throw new InvalidDataException("Metadata references a missing script: " + path);
            }
        }
        foreach (var (name, path) in session.Textures)
        {
            if (_manager.Textures.GetValueOrDefault(name) != path && !Available(path))
            {
                throw new InvalidDataException("Metadata references a missing atlas: " + path);
            }
        }
    }

    private static void Replace(System.Text.Json.Nodes.JsonObject target, System.Text.Json.Nodes.JsonObject source)
    {
        target.Clear();
        foreach (var (key, value) in source)
        {
            target.Add(key, value?.DeepClone());
        }
    }

    public void SaveArchives(string destination)
    {
        BuildMod(Path.GetFileName(destination)).ApplyTo(_manager, destination);
    }

    public IReadOnlyList<string> ValidateMod(ModPackage mod)
    {
        return mod.Validate(_manager);
    }

    public void ApplyMod(ModPackage mod, string destination)
    {
        mod.ApplyTo(_manager, destination);
    }

    public void Dispose()
    {
        foreach (var document in _documents.Values)
        {
            document.PropertyChanged -= DocumentChanged;
        }

        _documents.Clear();
        _manager.UnloadArchives();
        _decompiler?.Dispose();
    }
}
