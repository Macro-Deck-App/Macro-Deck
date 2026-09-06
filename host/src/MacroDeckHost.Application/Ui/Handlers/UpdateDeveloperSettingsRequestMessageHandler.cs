using MacroDeck.Plugin.Protocol.Errors;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateDeveloperSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateDeveloperSettingsRequest, UpdateDeveloperSettingsResponse>
{
	private readonly IAppPreferenceService _service;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IMediator _mediator;

	public UpdateDeveloperSettingsRequestMessageHandler(
		IAppPreferenceService service,
		IPluginSessionRegistry sessionRegistry,
		IMediator mediator)
	{
		_service = service;
		_sessionRegistry = sessionRegistry;
		_mediator = mediator;
	}

	public async ValueTask<UpdateDeveloperSettingsResponse> Handle(
		UpdateDeveloperSettingsRequest request,
		CancellationToken cancellationToken)
	{
		// Read before writing: the change event and the session drop are both about the transition, and
		// SetDeveloper only reports the state it landed on.
		var wasEnabled = (await _service.GetDeveloper()).Enabled;
		var settings = await _service.SetDeveloper(request.Enabled);

		if (settings.Enabled == wasEnabled)
		{
			return new UpdateDeveloperSettingsResponse { Enabled = settings.Enabled };
		}

		if (!settings.Enabled)
		{
			await DropDevelopmentSessions(cancellationToken);
		}

		await _mediator.Publish(new DeveloperModeChangedNotification(settings.Enabled), cancellationToken);

		return new UpdateDeveloperSettingsResponse { Enabled = settings.Enabled };
	}

	/// <summary>
	/// Switching Developer Mode off has to reach sessions that are already open, or the plugins it is
	/// meant to stop keep running until they happen to reconnect. Only self-registered sessions go -
	/// a plugin Macro Deck installed and launched holds a managed session and is not developer tooling.
	/// </summary>
	private async Task DropDevelopmentSessions(CancellationToken cancellationToken)
	{
		var pluginIds = _sessionRegistry.Snapshot()
			.Where(session => session.Origin == PluginSessionOrigin.SelfRegistered &&
				session.State != PluginSessionState.Dropped)
			.Select(session => session.PluginId)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var dropped = false;
		foreach (var pluginId in pluginIds)
		{
			if (await _sessionRegistry.TerminateForPlugin(pluginId,
				ProtocolCloseCodes.AuthenticationFailed,
				"Developer Mode disabled."))
			{
				dropped = true;
			}
		}

		if (dropped)
		{
			await _mediator.Publish(new PluginSessionsChangedNotification(), cancellationToken);
		}
	}
}
