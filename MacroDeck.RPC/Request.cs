using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MacroDeck.RPC;

public class Request
{
    // ReSharper disable once StringLiteralTypo
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; }
    [JsonPropertyName("params")] public object? Params { get; set; }
    [JsonIgnore] public JsonNode? ParamsNode => JsonSerializer.SerializeToNode(Params);
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonIgnore] public bool IsNotification => Id == null;
    
    public Request(string jsonRpc, string method, object? @params = null, string? id = null)
    {
        Id = id;
        JsonRpc = jsonRpc ?? throw new JsonException();
        Method = method ?? throw new JsonException();
        Params = @params;
    }
}
