using EastwardLib.Assets;
using EastwardLib.Utils;
using System.Text.Json.Nodes;

namespace EastwardModTool.Core.Documents;

public sealed class ResourceDocument : EditableDocument<Asset>
{
    public Asset Current => State;
    public string? PreviewWarning
    {
        get; internal set;
    }
    public bool CanAnimate => State is MSpriteAsset or SpriteSequenceAsset;
    public IReadOnlyList<string> Entries => State switch
    {
        DeckAsset deck => deck.Entries,
        LegacyDeckAsset legacy => legacy.Entries,
        LegacyDeckPackAsset legacyPack => legacyPack.DeckNames,
        NamedTilesetAsset tiles => tiles.Entries,
        MultiTextureAsset multi => multi.Entries,
        TilesetPackAsset tileset => ((JsonAsset)tileset.Items["tileset.json"]).Data!["themes"]!.AsArray().Select(theme => theme!["name"]!.ToString()).ToArray(),
        SpriteSequenceAsset sequence => [sequence.AnimationName],
        MSpriteAsset sprite => sprite.Data!["anims"]!.AsObject().Select(pair => pair.Value!["name"]!.ToString()).Distinct(StringComparer.Ordinal).ToArray(),
        TexturePackAsset textures => textures.Textures.Keys.ToArray(),
        PackageAsset package => package.Items.OrderBy(pair => pair.Value is HmgAsset ? 0 : 1).Select(pair => pair.Key).ToArray(),
        _ => []
    };

    internal ResourceDocument(string name, Asset original, bool canEdit) : base(name, original, original, canEdit) { }

    public override Asset Compile()
    {
        RequireEditable();
        return State;
    }

    public override void ImportAsset(Asset asset)
    {
        RequireEditable();
        Change(asset);
    }

    public IReadOnlyDictionary<string, JsonNode?> SceneData => State switch
    {
        PackageAsset package when package.Items.Count > 0 => package.Items.Where(pair => pair.Value is JsonAsset).ToDictionary(pair => pair.Key, pair => ((JsonAsset)pair.Value).Data, StringComparer.Ordinal),
        SceneAsset scene => new Dictionary<string, JsonNode?> { ["Scene"] = scene.Index },
        JsonAsset json => new Dictionary<string, JsonNode?> { ["Scene"] = json.Data },
        _ => new Dictionary<string, JsonNode?>()
    };

    public void ReplaceSceneData(string name, JsonNode? data)
    {
        RequireEditable();
        if (!SceneData.TryGetValue(name, out var previous))
        {
            throw new InvalidOperationException("Scene group was removed: " + name);
        }

        if (JsonNode.DeepEquals(previous, data))
        {
            return;
        }

        var replacement = new JsonAsset(data?.DeepClone());
        if (State is PackageAsset package && package.Items.Count > 0)
        {
            var items = package.Items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            items[name] = replacement;
            Change(new PackageAsset(items));
        }
        else
        {
            Change(replacement);
        }
    }

    public RgbaImage? GetImage(string? entry = null, int frame = 0, string? channel = null)
    {
        if (AssetType == "lut_texture" && State is BinaryAsset bytes)
        {
            try
            {
                var data = bytes.Encode();
                if (data.AsSpan().StartsWith("PGF"u8))
                {
                    return Asset.Create<HmgAsset>(data).Image;
                }

                using var stream = new MemoryStream(data);
                return RgbaImage.Load(stream);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException) { return null; }
        }
        if (State is LegacyDeckAsset legacy)
        {
            return legacy.Render(entry);
        }

        if (State is LegacyDeckPackAsset legacyPack)
        {
            return legacyPack.GetDeck(entry ?? legacyPack.DeckNames[0]).Render(null);
        }

        if (State is NamedTilesetAsset tiles)
        {
            return tiles.Render(entry);
        }

        if (State is TilesetPackAsset tileset)
        {
            var theme = ((JsonAsset)tileset.Items["tileset.json"]).Data!["themes"]!.AsArray().FirstOrDefault(theme => entry == null || theme!["name"]!.ToString() == entry);
            return theme == null ? null : new NamedTilesetAsset(theme, ((HmgAsset)tileset.Items["atlas.png"]).Image!).Render(null);
        }
        if (State is DeckAsset deck)
        {
            return deck.Render(entry, channel);
        }

        if (State is SpriteSequenceAsset sequence)
        {
            return GetFrameCount(sequence.AnimationName) == 0 ? null : sequence.RenderFrame(frame);
        }

        if (State is MSpriteAsset sprite)
        {
            entry ??= Entries.FirstOrDefault();
            return entry == null || GetFrameCount(entry) == 0 ? null : sprite.RenderFrame(entry, frame);
        }
        if (State is HmgAsset image)
        {
            return image.Image;
        }

        if (State is TexturePackAsset textures)
        {
            return textures.Textures.GetValueOrDefault(entry ?? textures.Textures.Keys.FirstOrDefault() ?? "");
        }

        if (State is PackageAsset package)
        {
            var item = entry == null ? package.Items.Values.OfType<HmgAsset>().FirstOrDefault() : package.Items.GetValueOrDefault(entry) as HmgAsset;
            return item?.Image;
        }
        return null;
    }

    public AssetDocument OpenItem(string name)
    {
        if (State is MultiTextureAsset multi)
        {
            return new ResourceDocument(Name + "/" + name, new HmgAsset(multi.GetImage(name) ?? throw new InvalidDataException("Missing multi-texture image")), false);
        }

        if (State is TexturePackAsset textures)
        {
            return new ResourceDocument(Name + "/" + name, new HmgAsset(textures.Textures[name]), false);
        }

        if (State is LegacyDeckPackAsset legacy)
        {
            return new ResourceDocument(Name + "/" + name, legacy.GetDeck(name), false);
        }

        if (State is TilesetPackAsset tiles)
        {
            var theme = ((JsonAsset)tiles.Items["tileset.json"]).Data!["themes"]!.AsArray().First(theme => theme!["name"]!.ToString() == name)!;
            return new ResourceDocument(Name + "/" + name, new NamedTilesetAsset(theme, ((HmgAsset)tiles.Items["atlas.png"]).Image!), false);
        }
        if (State is not PackageAsset package)
        {
            throw new InvalidOperationException("Open a package first");
        }

        var asset = package.Items[name];
        if (asset is BinaryAsset binary && Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
        {
            using var input = new MemoryStream(binary.Encode());
            asset = new HmgAsset(RgbaImage.Load(input));
        }
        AssetDocument document = asset is TextAsset or JsonAsset && asset is not MSpriteAsset ? new TextDocument(Name + "/" + name, asset, false) : new ResourceDocument(Name + "/" + name, asset, false);
        document.AssetType = asset is JsonAsset ? "data_json" : "file";
        return document;
    }

    public ResourceDocument OpenDeck(string name)
    {
        if (State is not DeckPackAsset pack)
        {
            throw new InvalidOperationException("Open a deck pack first");
        }

        return new ResourceDocument(Name + "/" + name, pack.GetDeck(name), false);
    }

    public int GetFrameCount(string entry)
    {
        if (State is SpriteSequenceAsset sequence)
        {
            return sequence.Data!["animation"]!["seq"]!.AsArray().Count;
        }

        return State is MSpriteAsset sprite ? sprite.Data!["anims"]!.AsObject().Select(pair => pair.Value!).First(animation => animation["name"]!.ToString() == entry)["seq"]!.AsArray().Count : 1;
    }

    public TimeSpan GetFrameDuration(string entry, int frame)
    {
        var sprite = State is SpriteSequenceAsset parentSequence ? parentSequence.Sprite : State as MSpriteAsset;
        if (sprite == null)
        {
            throw new NotSupportedException("Only sprites have animation timing");
        }

        if (State is SpriteSequenceAsset selected)
        {
            entry = selected.AnimationName;
        }

        var animation = sprite.Data!["anims"]!.AsObject().Select(pair => pair.Value!).First(value => value["name"]!.ToString() == entry);
        var sequence = animation["seq"]!.AsArray();
        if (frame < 0 || frame >= sequence.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frame));
        }

        var seconds = double.Parse(sequence[frame]![1]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        // Zero-duration frames occur in exported sprite data; keep preview playback responsive.
        return TimeSpan.FromSeconds(double.IsFinite(seconds) && seconds > 0 ? Math.Clamp(seconds, 0.001, 86400) : 1.0 / 60);
    }

    protected override void StateChanged()
    {
        base.StateChanged();
        Notify(nameof(Current));
        Notify(nameof(Entries));
    }

    protected override long EstimateSize(Asset value)
    {
        return value switch
        {
            HmgAsset image => image.Image?.Pixels.LongLength ?? 0,
            MSpriteAsset sprite => sprite.AtlasImages.Values.Sum(image => image.Pixels.LongLength) + (sprite.Data?.ToJsonString().Length ?? 0) * 2L,
            PackageAsset package => package.Items.Values.Sum(EstimateSize),
            TextAsset text => text.Text.Length * 2L,
            JsonAsset json => (json.Data?.ToJsonString().Length ?? 0) * 2L,
            _ => value.Encode().LongLength
        };
    }
}
