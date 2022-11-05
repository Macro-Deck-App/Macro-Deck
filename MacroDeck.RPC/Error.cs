using MacroDeck.RPC.Enum;

namespace MacroDeck.RPC;

public class Error
{
    public ErrorCode Code { get; set; }
    public string Message { get; set; }
    public object? Data { get; set; }

    public Error(ErrorCode code, string message, object? data = null)
    {
        Code = code;
        Message = message;
        Data = data;
    }
}