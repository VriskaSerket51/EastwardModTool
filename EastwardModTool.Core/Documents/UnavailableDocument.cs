using EastwardLib.Assets;

namespace EastwardModTool.Core.Documents;

public sealed class UnavailableDocument : AssetDocument
{
    public string Reason
    {
        get;
    }
    public override bool IsDirty => false;
    public override bool CanUndo => false;
    public override bool CanRedo => false;
    internal override object CurrentState => Reason;
    internal UnavailableDocument(string name, string reason) : base(name, new BinaryAsset([]), false) { Reason = reason; }
    public override Asset Compile()
    {
        throw new NotSupportedException(Reason);
    }
    public override void ImportAsset(Asset asset)
    {
        throw new NotSupportedException(Reason);
    }
    public override void Undo()
    {
    }
    public override void Redo()
    {
    }
    public override void Reset()
    {
    }
    public override void ExportSource(string path)
    {
        throw new NotSupportedException(Reason);
    }
}
