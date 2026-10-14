using System.ComponentModel;
using System.Runtime.CompilerServices;
using EastwardLib.Assets;

namespace EastwardModTool.Core.Documents;

public abstract class AssetDocument : INotifyPropertyChanged
{
    protected Asset Original
    {
        get;
    }
    public string Name
    {
        get;
    }
    public bool CanEdit
    {
        get;
    }
    public abstract bool IsDirty
    {
        get;
    }
    public abstract bool CanUndo
    {
        get;
    }
    public abstract bool CanRedo
    {
        get;
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    protected AssetDocument(string name, Asset original, bool canEdit)
    {
        Name = name;
        Original = original;
        CanEdit = canEdit;
    }

    public abstract Asset Compile();
    public abstract void ImportAsset(Asset asset);
    public Asset ReadSource(string path)
    {
        RequireEditable();
        return Original.CompileSource(path);
    }
    public void ImportSource(string path)
    {
        ImportAsset(ReadSource(path));
    }

    public Asset Baseline => Original;
    public string AssetType { get; internal set; } = "unknown";
    internal abstract object CurrentState
    {
        get;
    }
    public abstract void Undo();
    public abstract void Redo();
    public abstract void Reset();

    public virtual void ExportSource(string path)
    {
        var asset = IsDirty ? Compile() : Original;
        switch (asset)
        {
            case MSpriteAsset sprite:
                sprite.SaveSourceTo(path);
                break;
            case LuaAsset lua when !lua.IsBytecode:
                lua.SaveSourceTo(path);
                break;
            case GraphAsset graph:
                graph.SaveSourceTo(path);
                break;
            default:
                asset.SaveTo(path);
                break;
        }
    }

    protected void RequireEditable()
    {
        if (!CanEdit)
        {
            throw new NotSupportedException("Edit this virtual asset through its source package: " + Name);
        }
    }

    protected void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
