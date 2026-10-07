using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using Mediator;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ProfileCreatedNotificationHandler : INotificationHandler<ProfileCreatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly DeviceLayoutConstraintTracker _layoutConstraints;
	private readonly IColorReferenceResolver _colors;

	public ProfileCreatedNotificationHandler(IUiTransport uiTransport,
		DeviceLayoutConstraintTracker layoutConstraints,
		IColorReferenceResolver colors)
	{
		_colors = colors;
		_uiTransport = uiTransport;
		_layoutConstraints = layoutConstraints;
	}

	public async ValueTask Handle(ProfileCreatedNotification notification, CancellationToken cancellationToken)
	{
		var constraint = _layoutConstraints.Get(notification.Profile.Id.ToString());
		var evt = new ProfileCreatedEvent
			{ Profile = ProfileDtoMapper.MapJsonProfile(notification.Profile, constraint, _colors) };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
