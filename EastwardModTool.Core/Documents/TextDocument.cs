using System.Text;
using System.Text.Json.Nodes;
using EastwardLib.Assets;

namespace EastwardModTool.Core.Documents;

public class TextDocument : EditableDocument<string>
{
    public string Text
    {
        get => State;
        set => Change(value);
    }

    internal TextDocument(string name, Asset original, bool canEdit) : base(name, original, ReadText(original), canEdit) { }

    public override Asset Compile()
    {
        RequireEditable();
        if (Original is WorkbookAsset workbook)
        {
            var data = JsonNode.Parse(Text) ?? throw new InvalidDataException("Empty workbook data");
            return workbook.CompileData(data);
        }
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(Text));
        return Original.CompileSource(source);
    }

    public override void ImportAsset(Asset asset)
    {
        RequireEditable();
        Change(ReadText(asset));
    }

    protected override void StateChanged()
    {
        base.StateChanged();
        Notify(nameof(Text));
    }

    private static string ReadText(Asset asset)
    {
        return asset switch
        {
            SQScriptAsset sq => sq.GetSource(),
            CsvAsset csv => csv.ToCsv(),
            GraphAsset graph => graph.GetGraphMl(),
            LuaAsset lua => lua.GetSource(),
            TextAsset text => text.Text,
            JsonAsset json => json.ToJson(),
            _ => throw new NotSupportedException("This asset does not expose text source")
        };
    }

    protected override long EstimateSize(string value)
    {
        return value.Length * 2L;
    }
}
