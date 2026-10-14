using System.Text.Json.Nodes;

namespace EastwardModTool.Editing;

/// <summary>Resolves local serializer IDs without replacing the original object map.</summary>
public sealed class SerializedObjectGraph
{
    public JsonNode? Data
    {
        get;
    }
    public JsonObject? Objects
    {
        get;
    }
    public string? RootId
    {
        get;
    }
    public SerializedObjectGraph(JsonNode? data)
    {
        Data = data;
        if (data is JsonObject obj)
        {
            Objects = obj["map"] as JsonObject;
            RootId = obj["root"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
        }
    }
    public JsonObject? Object(string id) => Objects?[id] as JsonObject;
    public JsonNode? Body(string id) => Object(id)?["body"];
    public string Label(string id)
    {
        var obj = Object(id);
        if (obj == null)
        {
            return id + " (missing object)";
        }

        if (obj["alias"] is JsonValue alias && alias.TryGetValue<string>(out var target))
        {
            return "Alias → " + target;
        }

        if (obj["model"] is JsonValue raw && raw.TryGetValue<bool>(out var modeled) && !modeled)
        {
            var value = obj["body"]?.ToString() ?? "null";
            return (value.Length > 60 ? value[..60] + "…" : value) + " · value";
        }
        var model = obj["model"]?.ToString() ?? "Object";
        var type = model[(model.LastIndexOf('.') + 1)..];
        var body = obj["body"] as JsonObject;
        var name = body?["name"]?.ToString() ?? body?["_fullname"]?.ToString();
        return string.IsNullOrEmpty(name) ? type : name + " · " + type;
    }
    public string? Reference(JsonNode? value)
    {
        return value is JsonValue text && text.TryGetValue<string>(out var id) && Object(id) != null ? id : null;
    }
    public IEnumerable<string> References(string owner, string field)
    {
        return Body(owner) is JsonObject body && body[field] is JsonArray array ? array.Select(Reference).OfType<string>() : [];
    }
}
