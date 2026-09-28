using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Announcements;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetPendingAnnouncementRequestMessageHandler
	: IUiTransportMessageHandler<GetPendingAnnouncementRequest, GetPendingAnnouncementResponse>
{
	private readonly IAnnouncementService _announcements;

	public GetPendingAnnouncementRequestMessageHandler(IAnnouncementService announcements)
	{
		_announcements = announcements;
	}

	public ValueTask<GetPendingAnnouncementResponse> Handle(GetPendingAnnouncementRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetPendingAnnouncementResponse { Announcement = _announcements.Pending });
}
