using System.Net.WebSockets;

namespace SuchByte.MacroDeck.DataTypes;

public class WebSocketCloseReason
{
    public WebSocketCloseStatus Status { get; }
    public string Description { get; }

    public WebSocketCloseReason(WebSocketCloseStatus status, string description)
    {
        Status = status;
        Description = description;
    }
}
