using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeck.Server.Controllers;

public class WebSocketController : ControllerBase
{

    public WebSocketController()
    {
    }

    [Route("/")]
    [HttpGet]
    public async Task Get()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await WebSocketHandler.HandleWebSocket(webSocket);
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}