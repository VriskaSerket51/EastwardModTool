using System.Globalization;
using System.Text;
using EastwardLib.Assets;
using System.Text.Json.Nodes;
using EastwardLib.Compilation;
using EastwardModTool.Core.Documents;

namespace EastwardModTool.Core;

public static class LanguageScriptPatcher
{
    private const string Marker = "-- EastwardModTool.LanguagePatch.v1";
    private const string OptionsPrefix = "-- options:";
    private const string SourcePrefix = "local __ew_original_source = ";
    private static readonly (string Name, string Kind)[] Targets =
    [
        ("script/EWGlobalManager.lua", "manager"),
        ("script/ui/SettingMenuController.lua", "settings"),
        ("farm/script/ui/FarmSettingMenuController.lua", "farm-settings"),
        ("script/ui/LanguageSelectionController.lua", "initial"),
        ("script/ui/DialogController.lua", "dialog"),
        ("script/ui/ChapterTitleController.lua", "chapter"),
        ("script/ui/TitleMenuController.lua", "title")
    ];

    public static IReadOnlyDictionary<string, string> CreatePatches(GameWorkspace workspace, LanguagePatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);
        var configuration = ToConfiguration(options);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, kind) in Targets)
        {
            if (kind == "initial" && !options.IncludeInitialMenu)
            {
                continue;
            }
            if (!workspace.Assets.ContainsKey(name))
            {
                if (kind == "farm-settings")
                {
                    continue;
                }
                throw new InvalidDataException("Missing language runtime script: " + name);
            }
            var document = workspace.Documents.FirstOrDefault(document => document.Name == name);
            var asset = document?.IsDirty == true ? document.Compile() : workspace.LoadOriginalAsset(name);
            if (asset is not LuaAsset lua)
            {
                throw new InvalidDataException("Language runtime asset is not Lua: " + name);
            }
            result.Add(name, PatchSource(lua.Encode(), name, kind, configuration));
        }
        return result;
    }

    private static JsonObject ToConfiguration(LanguagePatchOptions options)
    {
        return new JsonObject
        {
            ["language"] = options.Language,
            ["displayName"] = options.DisplayName,
            ["baseLanguage"] = options.BaseLanguage,
            ["dialogClass"] = options.DialogClass,
            ["styleSheetAsset"] = options.StyleSheetAsset,
            ["fontAsset"] = options.FontAsset,
            ["textureRemappings"] = new JsonObject(options.TextureRemappings.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
            ["originalFonts"] = new JsonArray(options.OriginalFontAssets.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["chapterGroup"] = options.ChapterGroup,
            ["titleTexture"] = options.TitleTexture,
            ["dialogLineSpacing"] = options.DialogLineSpacing,
            ["firstRunButtonSpacing"] = options.FirstRunButtonSpacing,
            ["includeInitialMenu"] = options.IncludeInitialMenu
        };
    }

    private static string PatchSource(byte[] payload, string name, string kind, JsonObject configuration)
    {
        var configurations = new JsonArray();
        if (payload.AsSpan().StartsWith(Encoding.UTF8.GetBytes(Marker + "\n")))
        {
            var source = Encoding.UTF8.GetString(payload);
            var headerEnd = source.IndexOf('\n', Marker.Length + 1);
            if (headerEnd < 0)
            {
                throw new InvalidDataException("Generated language patch was truncated: " + name);
            }
            var header = source[(Marker.Length + 1)..headerEnd];
            if (!header.StartsWith(OptionsPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid generated language patch header: " + name);
            }
            configurations = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header[OptionsPrefix.Length..])))!.AsArray();
            var start = headerEnd + 1;
            if (!source.AsSpan(start).StartsWith(SourcePrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid generated language source: " + name);
            }
            start += SourcePrefix.Length;
            payload = ReadByteLiteral(source, start, name);
        }
        for (var i = configurations.Count - 1; i >= 0; i--)
        {
            if (configurations[i]?["language"]?.ToString() == configuration["language"]?.ToString())
            {
                configurations.RemoveAt(i);
            }
        }
        configurations.Add(configuration.DeepClone());
        using var stream = typeof(LanguageScriptPatcher).Assembly.GetManifestResourceStream("EastwardModTool.Core.Localization.LanguagePatchRuntime.lua")
            ?? throw new InvalidOperationException("Language patch runtime resource is missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var runtime = reader.ReadToEnd();
        return Marker + "\n" + OptionsPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(configurations.ToJsonString())) + "\n"
            + SourcePrefix + ByteLiteral(payload) + "\n"
            + "local __ew_chunk = assert(loadstring(__ew_original_source, " + LocaleCompiler.WriteLua(JsonValue.Create("@" + name)!) + "))\n"
            + "setfenv(__ew_chunk, getfenv(1))\n__ew_chunk()\n"
            + "local __ew_runtime = (function()\n" + runtime + "\nend)()\n"
            + "for _, options in ipairs(" + LocaleCompiler.WriteLua(configurations) + ") do\n"
            + "    __ew_runtime.apply(" + LocaleCompiler.WriteLua(JsonValue.Create(kind)!) + ", options, getfenv(1))\nend\n";
    }

    private static string ByteLiteral(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 4 + 2);
        text.Append('"');
        foreach (var value in bytes)
        {
            text.Append('\\').Append(value.ToString("D3", CultureInfo.InvariantCulture));
        }
        return text.Append('"').ToString();
    }

    private static byte[] ReadByteLiteral(string source, int start, string name)
    {
        if (start >= source.Length || source[start] != '"')
        {
            throw new InvalidDataException("Invalid generated Lua payload: " + name);
        }
        var bytes = new List<byte>();
        var position = start + 1;
        while (position < source.Length && source[position] != '"')
        {
            if (position + 3 >= source.Length || source[position] != '\\'
                || !byte.TryParse(source.AsSpan(position + 1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidDataException("Invalid generated Lua byte escape: " + name);
            }
            bytes.Add(value);
            position += 4;
        }
        if (position >= source.Length)
        {
            throw new InvalidDataException("Generated Lua payload was truncated: " + name);
        }
        return bytes.ToArray();
    }
}
