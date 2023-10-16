using Microsoft.AspNetCore.Mvc;

namespace MacroDeck.Server.Controllers;

public class PingPongController : ControllerBase
{
    [HttpGet("/ping")]
    public string Ping()
    {
        return "Pong";
    }
}