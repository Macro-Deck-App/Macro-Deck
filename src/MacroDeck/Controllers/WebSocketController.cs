using Microsoft.AspNetCore.Mvc;

namespace SuchByte.MacroDeck.Controllers;

public class WebSocketController : ControllerBase
{
    [Route("/")]
    [HttpGet]
    public async Task<ActionResult> Get()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return Redirect("client");
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await WebSocketHandler.HandleWebSocket(webSocket);
        return new EmptyResult();
    }
}
