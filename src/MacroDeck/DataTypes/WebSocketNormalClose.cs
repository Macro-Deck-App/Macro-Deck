using System.Net.WebSockets;

namespace SuchByte.MacroDeck.DataTypes;

public class WebSocketNormalClose : WebSocketCloseReason
{
    public WebSocketNormalClose()
        : base(WebSocketCloseStatus.NormalClosure, "Closing")
    {
    }
}
