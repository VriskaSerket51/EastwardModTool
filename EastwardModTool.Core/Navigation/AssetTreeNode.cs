using System.ComponentModel;
using EastwardLib.MetaData;

namespace EastwardModTool.Core.Navigation;

public sealed class AssetTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private readonly List<AssetTreeNode> _children = [];
    public string Key { get; }
    public string Name { get; }
    public AssetInfo? Asset { get; }
    public bool IsAsset => Asset != null && Asset.Type != "folder";
    public bool IsMatch { get; }
    public AssetTreeNode? Parent { get; internal set; }
    public IReadOnlyList<AssetTreeNode> Children => _children;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal AssetTreeNode(string key, string name, AssetInfo? asset = null, bool isMatch = false)
    {
        Key = key;
        Name = name;
        Asset = asset;
        IsMatch = isMatch;
    }

    internal void Add(AssetTreeNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void Sort()
    {
        _children.Sort(Compare);
        foreach (var child in _children) child.Sort();
    }

    internal static int Compare(AssetTreeNode first, AssetTreeNode second)
    {
        var directory = first.IsAsset.CompareTo(second.IsAsset);
        if (directory != 0) return directory;
        var name = StringComparer.OrdinalIgnoreCase.Compare(first.Name, second.Name);
        return name != 0 ? name : StringComparer.Ordinal.Compare(first.Key, second.Key);
    }
}
