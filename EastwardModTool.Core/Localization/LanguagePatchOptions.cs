namespace EastwardModTool.Core;

public sealed class LanguagePatchOptions
{
    public string Language { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BaseLanguage { get; set; } = "en";
    public string? TranslationFile
    {
        get; set;
    }
    public string? LocaleDirectory
    {
        get; set;
    }
    public Dictionary<string, string> SourceFiles { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LocalizedImages { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TextureRemappings { get; set; } = new(StringComparer.Ordinal);
    public string? FontAsset
    {
        get; set;
    }
    public string? FontSource
    {
        get; set;
    }
    public string? StyleSheetAsset
    {
        get; set;
    }
    public List<string> OriginalFontAssets { get; set; } = [];
    public string? ChapterGroup
    {
        get; set;
    }
    public string? TitleTexture
    {
        get; set;
    }
    public string? DialogClass
    {
        get; set;
    }
    public double? DialogLineSpacing
    {
        get; set;
    }
    public double? FirstRunButtonSpacing
    {
        get; set;
    }
    public bool IncludeInitialMenu { get; set; } = true;

    internal LanguagePatchOptions Copy()
    {
        var copy = (LanguagePatchOptions)MemberwiseClone();
        copy.SourceFiles = new Dictionary<string, string>(SourceFiles, StringComparer.Ordinal);
        copy.LocalizedImages = new Dictionary<string, string>(LocalizedImages, StringComparer.Ordinal);
        copy.TextureRemappings = new Dictionary<string, string>(TextureRemappings, StringComparer.Ordinal);
        copy.OriginalFontAssets = [.. OriginalFontAssets];
        return copy;
    }
}
