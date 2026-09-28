using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Announcements;

namespace MacroDeckHost.Application.Ui.Handlers;

public class MarkAnnouncementSeenRequestMessageHandler
	: IUiTransportMessageHandler<MarkAnnouncementSeenRequest, MarkAnnouncementSeenResponse>
{
	private readonly IAnnouncementService _announcements;

	public MarkAnnouncementSeenRequestMessageHandler(IAnnouncementService announcements)
	{
		_announcements = announcements;
	}

	public async ValueTask<MarkAnnouncementSeenResponse> Handle(MarkAnnouncementSeenRequest request,
		CancellationToken cancellationToken)
	{
		await _announcements.MarkSeen(request.Number, cancellationToken);
		return new MarkAnnouncementSeenResponse();
	}
}
