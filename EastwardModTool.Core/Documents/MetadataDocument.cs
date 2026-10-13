using EastwardLib.Assets;
using EastwardLib.MetaData;

namespace EastwardModTool.Core.Documents;

public sealed class MetadataDocument : TextDocument
{
    internal MetadataDocument(string name, JsonAsset original) : base(name, original, true) { }

    public override Asset Compile()
    {
        switch (Name)
        {
            case "@asset_index":
                AssetIndex.Create(Text);
                break;
            case "@script_library":
                ScriptLibrary.Create(Text);
                break;
            case "@texture_index":
                TextureIndex.Create(Text);
                break;
            default:
                throw new InvalidDataException("Unknown metadata document: " + Name);
        }
        return base.Compile();
    }
}
