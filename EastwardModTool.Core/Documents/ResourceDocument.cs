using EastwardLib.Assets;
using EastwardLib.Utils;

namespace EastwardModTool.Core.Documents;

public sealed class ResourceDocument : EditableDocument<Asset>
{
    public Asset Current => State;
    public IReadOnlyList<string> Entries => State switch
    {
        MSpriteAsset sprite => sprite.Data!["anims"]!.AsObject().Select(pair => pair.Value!["name"]!.ToString()).Distinct(StringComparer.Ordinal).ToArray(),
        TexturePackAsset textures => textures.Textures.Keys.ToArray(),
        PackageAsset package => package.Items.Keys.ToArray(),
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

    public RgbaImage? GetImage(string? entry = null, int frame = 0)
    {
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

    public int GetFrameCount(string entry)
    {
        return State is MSpriteAsset sprite ? sprite.Data!["anims"]!.AsObject().Select(pair => pair.Value!).First(animation => animation["name"]!.ToString() == entry)["seq"]!.AsArray().Count : 1;
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
