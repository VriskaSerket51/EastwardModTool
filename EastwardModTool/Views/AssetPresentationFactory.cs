using System.Text.Json.Nodes;
using Avalonia.Controls;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

internal static class AssetPresentationFactory
{
    public static Control? Create(ResourceDocument document, string? selected)
    {
        var asset = document.Current;
        if (asset is BitmapFontAsset bitmapFont)
        {
            return new BitmapFontView(bitmapFont);
        }

        if (asset is LegacyDeckAsset polygon && polygon.Data?["type"]?.ToString() == "polygon")
        {
            return new ShapePreviewView(polygon);
        }

        if (asset is LegacyDeckAsset patch && patch.Data?["type"]?.ToString() == "stretchpatch" && patch.Texture != null)
        {
            return new StretchPatchView(patch);
        }

        if (asset is MeshAsset mesh)
        {
            return new MeshPreviewView(mesh);
        }

        if (document.AssetType == "scene")
        {
            return new SceneHierarchyView(document);
        }
        if (asset is LocalePackAsset locale)
        {
            return new TableDataView(() => LocaleRows(locale));
        }

        if (asset is PackageAsset or LegacyDeckPackAsset or MultiTextureAsset)
        {
            return new PackageBrowserView(document, selected);
        }

        if (asset is BinaryAsset binary && document.AssetType == "font_ttf")
        {
            try
            {
                return new FontAssetView(binary);
            }
            catch (InvalidDataException) { return new BinaryAssetView(binary, document.AssetType); }
        }
        if (asset is BinaryAsset bytes && asset is not LuaAsset)
        {
            if (document.AssetType == "lut_texture" && document.GetImage() != null)
            {
                return null;
            }

            return new BinaryAssetView(bytes, document.AssetType);
        }
        return null;
    }
    private static IReadOnlyList<IReadOnlyList<string>> LocaleRows(LocalePackAsset locale)
    {
        var languages = locale.Languages.Keys.ToArray();
        var keys = locale.Languages.Values.SelectMany(data => data.AsObject().SelectMany(table => table.Value!.AsObject().Select(pair => (Table: table.Key, Key: pair.Key)))).Distinct().ToArray();
        return new[] { (IReadOnlyList<string>)new[] { "Table", "Key" }.Concat(languages).ToArray() }.Concat(keys.Select(key => (IReadOnlyList<string>)new[] { key.Table, key.Key }.Concat(languages.Select(language => locale.Languages[language][key.Table]?[key.Key]?.ToString() ?? "")).ToArray())).ToArray();
    }
}
