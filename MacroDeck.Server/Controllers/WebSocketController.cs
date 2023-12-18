using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeck.Server.Controllers;

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
        return Ok();
    }
}