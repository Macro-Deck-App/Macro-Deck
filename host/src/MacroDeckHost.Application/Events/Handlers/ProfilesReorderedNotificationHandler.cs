using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ProfilesReorderedNotificationHandler : INotificationHandler<ProfilesReorderedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ProfilesReorderedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(ProfilesReorderedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new ProfilesReorderedEvent
		{
			Profiles = notification.Profiles.Select(ProfileDtoMapper.MapToPlacement).ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
