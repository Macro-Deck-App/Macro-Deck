using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ProfileUpdatedNotificationHandler : INotificationHandler<ProfileUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly DeviceLayoutConstraintTracker _layoutConstraints;

	public ProfileUpdatedNotificationHandler(IUiTransport uiTransport, DeviceLayoutConstraintTracker layoutConstraints)
	{
		_uiTransport = uiTransport;
		_layoutConstraints = layoutConstraints;
	}

	public async ValueTask Handle(ProfileUpdatedNotification notification, CancellationToken cancellationToken)
	{
		var constraint = _layoutConstraints.Get(notification.Profile.Id.ToString());
		var evt = new ProfileUpdatedEvent
			{ Profile = ProfileDtoMapper.MapJsonProfile(notification.Profile, constraint) };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
