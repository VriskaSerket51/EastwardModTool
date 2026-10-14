using System.Xml;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace EastwardModTool.Editing;

/// <summary>Chooses syntax from the asset type, source filename and serialized text.</summary>
public static class AssetSyntax
{
    private static readonly IReadOnlyDictionary<string, Lazy<IHighlightingDefinition>> Definitions =
        new[] { "Lua", "GLSL", "ShaderScript", "SQ", "JSON", "XML", "BMFont", "OBJ", "CSV" }.ToDictionary(name => name, name => new Lazy<IHighlightingDefinition>(() => Load(name)), StringComparer.Ordinal);

    public static IHighlightingDefinition? ForSource(string name, string assetType, string source)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var language = assetType switch
        {
            "lua" or "com_script" or "ui_style" => "Lua",
            "shader_script" => "ShaderScript",
            "glsl" => "GLSL",
            "sq_script" => "SQ",
            "fsm_scheme" or "quest_scheme" or "scene_portal_graph" or "story_graph" => "XML",
            "data_csv" => "CSV",
            "font_bmfont" => "BMFont",
            "metadata" or "data_json" or "animator_data" or "proto" or "data_sheet" or "data_xls" or "asset_map" or "fs_folder" or "fs_event" => "JSON",
            _ => extension switch
            {
                ".lua" => "Lua",
                ".sq" => "SQ",
                ".shader_script" or ".shader" => "ShaderScript",
                ".glsl" or ".vert" or ".frag" or ".geom" or ".tesc" or ".tese" or ".comp" or ".vsh" or ".fsh" => "GLSL",
                ".json" => "JSON",
                ".xml" or ".ml" or ".graphml" or ".assxml" or ".xshd" or ".xaml" => "XML",
                ".fnt" => "BMFont",
                ".obj" or ".mtl" => "OBJ",
                ".csv" => "CSV",
                _ => null
            }
        };
        if (language == null)
        {
            var text = source.AsSpan().TrimStart();
            if (text.StartsWith("{") || text.StartsWith("["))
            {
                language = "JSON";
            }
            else if (text.StartsWith("<?xml") || text.StartsWith("<!--") || text.Length > 1 && text[0] == '<' && char.IsLetter(text[1]))
            {
                language = "XML";
            }
        }
        return language == null ? HighlightingManager.Instance.GetDefinitionByExtension(extension) : Definitions[language].Value;
    }

    public static TextEditor ReadOnlySource(string name, string assetType, string source)
    {
        return new TextEditor
        {
            Text = source,
            SyntaxHighlighting = ForSource(name, assetType, source),
            IsReadOnly = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code,Consolas,DejaVu Sans Mono"),
            FontSize = 13,
            Padding = new Thickness(16)
        };
    }

    private sealed class Resolver : IHighlightingDefinitionReferenceResolver
    {
        public IHighlightingDefinition? GetDefinition(string name)
        {
            return Definitions.TryGetValue(name, out var definition) ? definition.Value : HighlightingManager.Instance.GetDefinition(name);
        }
    }

    private static IHighlightingDefinition Load(string name)
    {
        using var stream = typeof(AssetSyntax).Assembly.GetManifestResourceStream($"EastwardModTool.Editing.Syntax.{name}.xshd")
            ?? throw new InvalidOperationException("Missing embedded syntax definition: " + name);
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, new Resolver());
    }
}
