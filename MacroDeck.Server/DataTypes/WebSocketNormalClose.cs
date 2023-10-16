using System.Net.WebSockets;

namespace MacroDeck.Server.DataTypes;

public class WebSocketNormalClose : WebSocketCloseReason
{
    public WebSocketNormalClose() 
        : base(WebSocketCloseStatus.NormalClosure, "Closing")
    {
    }
}