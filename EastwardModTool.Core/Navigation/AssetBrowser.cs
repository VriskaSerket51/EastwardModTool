using EastwardLib.MetaData;

namespace EastwardModTool.Core.Navigation;

public enum AssetBrowseMode
{
    Directories,
    Types
}

/// <summary>Project paths and asset types share a single selection identified by the indexed asset name.</summary>
public sealed class AssetBrowser
{
    private readonly IReadOnlyDictionary<string, AssetInfo> _assets;
    private readonly Dictionary<AssetBrowseMode, HashSet<string>> _expanded = new()
    {
        [AssetBrowseMode.Directories] = new(StringComparer.Ordinal),
        [AssetBrowseMode.Types] = new(StringComparer.Ordinal)
    };
    private Dictionary<string, AssetTreeNode> _nodes = new(StringComparer.Ordinal);
    private AssetBrowseMode _mode;
    public AssetBrowseMode Mode => _mode;
    public IReadOnlyList<AssetTreeNode> Roots { get; private set; } = [];
    public IReadOnlyList<string> Types { get; }
    public string? SelectedName { get; private set; }
    public AssetInfo? SelectedAsset => SelectedName == null ? null : _assets.GetValueOrDefault(SelectedName);
    public AssetTreeNode? SelectedNode => SelectedName == null ? null : _nodes.GetValueOrDefault("asset:" + SelectedName);
    public int MatchingCount { get; private set; }

    public AssetBrowser(IReadOnlyDictionary<string, AssetInfo> assets)
    {
        _assets = assets;
        Types = assets.Values.Where(info => info.Type != "folder").Select(info => info.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Update(AssetBrowseMode.Directories);
    }

    public void Select(string name)
    {
        if (!_assets.TryGetValue(name, out var info) || info.Type == "folder") throw new ArgumentException("Select an indexed asset", nameof(name));
        SelectedName = name;
        RevealSelection();
    }

    public void Update(AssetBrowseMode mode, string search = "", string? type = null)
    {
        var expanded = _expanded[_mode];
        foreach (var node in _nodes.Values)
        {
            if (node.IsExpanded) expanded.Add(node.Key); else expanded.Remove(node.Key);
        }
        _mode = mode;
        var matches = _assets.Values.Where(info => info.Type != "folder" && (type == null || info.Type == type) && info.AssetName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        MatchingCount = matches.Length;
        var matchedNames = matches.Select(info => info.AssetName).ToHashSet(StringComparer.Ordinal);
        var nodes = new Dictionary<string, AssetTreeNode>(StringComparer.Ordinal);
        var roots = new List<AssetTreeNode>();
        var building = new HashSet<string>(StringComparer.Ordinal);

        AssetTreeNode Include(string name)
        {
            var key = "asset:" + name;
            if (nodes.TryGetValue(key, out var existing)) return existing;
            if (!building.Add(name)) throw new InvalidDataException("Cyclic asset parent: " + name);
            _assets.TryGetValue(name, out var info);
            var node = new AssetTreeNode(key, info?.Name ?? name[(name.LastIndexOf('/') + 1)..], info, matchedNames.Contains(name));
            var parent = info?.Parent ?? DirectoryParent(name);
            if (mode == AssetBrowseMode.Types)
            {
                while (parent.Length > 0 && (!_assets.TryGetValue(parent, out var owner) || owner.Type == "folder"))
                {
                    parent = _assets.TryGetValue(parent, out var folder) ? folder.Parent : DirectoryParent(parent);
                }
                if (parent.Length == 0)
                {
                    var groupKey = "type:" + info!.Type;
                    if (!nodes.TryGetValue(groupKey, out var group))
                    {
                        group = new AssetTreeNode(groupKey, info.Type);
                        nodes.Add(groupKey, group);
                        roots.Add(group);
                    }
                    group.Add(node);
                }
                else Include(parent).Add(node);
            }
            else if (parent.Length == 0) roots.Add(node);
            else Include(parent).Add(node);
            nodes.Add(key, node);
            building.Remove(name);
            return node;
        }

        foreach (var info in matches) Include(info.AssetName);
        if (mode == AssetBrowseMode.Directories && search.Length == 0 && type == null)
        {
            foreach (var info in _assets.Values.Where(info => info.Type == "folder")) Include(info.AssetName);
        }
        roots.Sort(AssetTreeNode.Compare);
        foreach (var root in roots) root.Sort();
        _nodes = nodes;
        Roots = roots;
        foreach (var node in nodes.Values) node.IsExpanded = _expanded[mode].Contains(node.Key) || search.Length > 0 || type != null;
        RevealSelection();
    }

    private void RevealSelection()
    {
        for (var node = SelectedNode?.Parent; node != null; node = node.Parent) node.IsExpanded = true;
    }

    private static string DirectoryParent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }
}
