using EastwardLib.Assets;
using EastwardLib.Utils;

namespace EastwardModTool.Core.Documents;

/// <summary>New runtime files retained in the same draft and undo model as indexed assets.</summary>
public sealed class RuntimeResourcesDocument : ResourceDocument
{
    public const string DocumentName = "@language_resources";

    internal RuntimeResourcesDocument() : base(DocumentName, new RuntimePackage([]), true)
    {
        AssetType = "runtime_resources";
    }

    public void ReplaceResources(IReadOnlyDictionary<string, byte[]> resources)
    {
        ImportAsset(new RuntimePackage(resources.ToDictionary(pair => pair.Key, pair => (Asset)new BinaryAsset((byte[])pair.Value.Clone()), StringComparer.Ordinal)));
    }

    public IReadOnlyDictionary<string, byte[]> Resources => ((PackageAsset)Current).Items.ToDictionary(pair => pair.Key, pair => pair.Value.Encode(), StringComparer.Ordinal);

    private sealed class RuntimePackage(Dictionary<string, Asset> items) : PackageAsset(items)
    {
        public override Asset CompileSource(string path)
        {
            var items = new Dictionary<string, Asset>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(path, file).Replace('\\', '/');
                if (name.Split('/').Length < 2)
                {
                    throw new InvalidDataException("Runtime resources require an archive prefix: " + name);
                }
                SafePath.ResolveWithin(path, name);
                items.Add(name, new BinaryAsset(File.ReadAllBytes(file)));
            }
            return new RuntimePackage(items);
        }
    }
}
