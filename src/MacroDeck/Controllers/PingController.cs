using Microsoft.AspNetCore.Mvc;
using SuchByte.MacroDeck.DataTypes;

namespace SuchByte.MacroDeck.Controllers;

[Route("ping")]
public class PingController : ControllerBase
{
    [HttpGet]
    public ActionResult<PingResponse> Ping()
    {
        return new PingResponse();
    }
}
