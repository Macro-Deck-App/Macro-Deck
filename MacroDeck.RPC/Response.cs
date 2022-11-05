using System.Text.Json.Serialization;

namespace MacroDeck.RPC;

public class Response
{
    // ReSharper disable once StringLiteralTypo
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; } = "2.0";
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public Error? Error { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}