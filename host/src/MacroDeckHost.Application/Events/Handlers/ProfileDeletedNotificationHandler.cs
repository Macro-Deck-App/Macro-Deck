using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ProfileDeletedNotificationHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ProfileDeletedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new ProfileDeletedEvent { ProfileId = notification.ProfileId.ToString() };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
