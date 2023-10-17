using MacroDeck.Server.DataTypes;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeck.Server.Controllers;

[Route("ping")]
public class PingController : ControllerBase
{
    [HttpGet]
    public ActionResult<PingResponse> Ping()
    {
        return new PingResponse();
    }
}