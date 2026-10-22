using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Integrations;

public interface IIntegrationCapabilityCounter
{
	Task<IReadOnlyDictionary<string, int>> CountAsync(IIntegration integration, CancellationToken cancellationToken);
}

public sealed class IntegrationCapabilityCounter : IIntegrationCapabilityCounter
{
	private readonly IBatteryRegistry _batteries;
	private readonly ICalendarEventCache _calendars;
	private readonly IEmailInboxCache _inboxes;
	private readonly IWeatherRegistry _weather;
	private readonly IMusicPlayerRegistry _musicPlayers;
	private readonly IEventRegistry _events;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IFolderViewRegistry _folderViews;
	private readonly IScreenSaverRegistry _screenSavers;
	private readonly IDeviceRepository _devices;

	public IntegrationCapabilityCounter(
		IBatteryRegistry batteries,
		ICalendarEventCache calendars,
		IEmailInboxCache inboxes,
		IWeatherRegistry weather,
		IMusicPlayerRegistry musicPlayers,
		IEventRegistry events,
		IWidgetTypeRegistry widgetTypes,
		IFolderViewRegistry folderViews,
		IScreenSaverRegistry screenSavers,
		IDeviceRepository devices)
	{
		_batteries = batteries;
		_calendars = calendars;
		_inboxes = inboxes;
		_weather = weather;
		_musicPlayers = musicPlayers;
		_events = events;
		_widgetTypes = widgetTypes;
		_folderViews = folderViews;
		_screenSavers = screenSavers;
		_devices = devices;
	}

	public async Task<IReadOnlyDictionary<string, int>> CountAsync(
		IIntegration integration,
		CancellationToken cancellationToken)
	{
		var id = integration.Id;
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);

		foreach (var capability in ProvidedCapabilityCatalog.For(integration))
		{
			int? count = capability.Kind switch
			{
				CapabilityKinds.BatteryProvider => _batteries.Devices.Count(device => device.ProviderId == id),
				CapabilityKinds.Calendar => _calendars.Snapshot.Accounts
					.Where(account => account.Account.IntegrationId == id)
					.Sum(account => account.Calendars.Count),
				CapabilityKinds.EmailInbox => _inboxes.Snapshot.Inboxes.Count(inbox => inbox.Inbox.IntegrationId == id),
				CapabilityKinds.Weather => _weather.GetInstances().Count(station => station.IntegrationId == id),
				CapabilityKinds.MusicPlayer => _musicPlayers.GetInstances().Count(player => player.IntegrationId == id),
				CapabilityKinds.Events => _events.GetDefinitions()
					.Count(definition => definition.IsIntegrationProvider && definition.ProviderId == id),
				CapabilityKinds.WidgetTypeProvider => _widgetTypes.All.Count(type => type.ProviderId == id),
				CapabilityKinds.FolderViewProvider => _folderViews.GetAll().Count(view => view.ProviderId == id),
				CapabilityKinds.ScreenSaverProvider => _screenSavers.GetAll().Count(saver => saver.ProviderId == id),
				CapabilityKinds.VirtualProfiles => integration is IProfileProvider profiles && integration.IsInitialized
					? profiles.GetProfiles().Count
					: 0,
				CapabilityKinds.DeviceProvider => (await _devices.GetByProviderId(id)).Count,
				_ => null
			};

			if (count is { } value)
			{
				counts[capability.Kind] = value;
			}
		}

		return counts;
	}
}
