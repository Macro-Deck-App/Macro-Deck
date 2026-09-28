using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Announcements;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/announcements")]
public class AnnouncementsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetPendingAnnouncementRequest, GetPendingAnnouncementResponse> _getPending;
	private readonly IUiTransportMessageHandler<MarkAnnouncementSeenRequest, MarkAnnouncementSeenResponse> _markSeen;

	public AnnouncementsController(
		IUiTransportMessageHandler<GetPendingAnnouncementRequest, GetPendingAnnouncementResponse> getPending,
		IUiTransportMessageHandler<MarkAnnouncementSeenRequest, MarkAnnouncementSeenResponse> markSeen)
	{
		_getPending = getPending;
		_markSeen = markSeen;
	}

	[HttpGet("pending")]
	public Task<GetPendingAnnouncementResponse> GetPending(CancellationToken ct)
		=> _getPending.Handle(new GetPendingAnnouncementRequest(), ct).AsTask();

	[HttpPost("seen")]
	public Task<MarkAnnouncementSeenResponse> MarkSeen(MarkAnnouncementSeenRequest body, CancellationToken ct)
		=> _markSeen.Handle(body, ct).AsTask();
}
