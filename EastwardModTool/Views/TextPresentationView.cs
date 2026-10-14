using System.Text.Json.Nodes;
using Avalonia.Controls;
using EastwardLib.Assets;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Views;

public sealed class TextPresentationView : TabControl, IDisposable
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    private readonly Control? _preview;
    public bool HasPreview => _preview != null;
    public TextPresentationView(TextDocument document, Control source)
    {
        string title;
        if (document.AssetType == "metadata")
        {
            title = "Source";
        }
        else if (document.AssetType == "stylesheet")
        {
            _preview = new StyleSheetView(document);
            title = "Styles";
        }
        else if (document.AssetType == "code_tileset")
        {
            _preview = new CodeTilesetView(document);
            title = "Tiles";
        }
        else if (document.Baseline is GraphAsset)
        {
            _preview = new GraphPreviewView(document);
            title = "Graph";
        }
        else if (document.Baseline is WorkbookAsset or CsvAsset || document.AssetType is "data_sheet" or "fs_folder" or "asset_map")
        {
            _preview = new TableDataView(document);
            title = "Table";
        }
        else
        {
            JsonNode? data;
            try
            {
                data = JsonNode.Parse(document.Text);
            }
            catch (System.Text.Json.JsonException) { data = null; }
            if (data != null && document.AssetType is "proto" or "prefab")
            {
                _preview = new SceneHierarchyView(document);
                title = "Hierarchy";
            }
            else if (data is JsonObject obj && obj["map"] is JsonObject)
            {
                _preview = new EngineObjectView(document);
                title = "Objects";
            }
            else if (data != null)
            {
                _preview = new JsonDataView(document);
                title = "Properties";
            }
            else
            {
                title = "Source";
            }
        }
        Name = "TextPresentation";
        if (_preview != null)
        {
            Items.Add(new TabItem { Header = title, Content = _preview });
        }

        if (_preview != null)
        {
            Items.Add(new TabItem { Header = "Source", Content = source });
        }

        SelectedIndex = 0;
    }
    public void ShowSource()
    {
        SelectedIndex = Items.Count - 1;
    }
    public void Dispose()
    {
        if (_preview is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
