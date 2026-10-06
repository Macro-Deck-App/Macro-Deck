using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Calendar;

// Reads are never degraded to an empty answer: ICalendarProvider requires a failure to throw, so a
// RemoteCapabilityException reaches the caller as the account's read failure.
public sealed class RemoteCalendarProvider(
	string pluginId,
	string providerName,
	IReadOnlyList<CalendarAccount> accounts,
	IPluginCapabilityInvoker invoker) : ICalendarProvider
{
	private readonly ILogger _logger = Log.ForContext<RemoteCalendarProvider>();

	public string ProviderName => providerName;

	public IReadOnlyList<CalendarAccount> GetAccounts() => accounts;

	public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(
		string accountId,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Calendar.Calendars,
				new CalendarAccountArguments { AccountId = accountId },
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<CalendarListResult>(PluginProtocolJson.Options);
		return result is null ? [] : [.. result.Calendars.Select(CalendarMapper.ToDomain)];
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);

		var data = await InvokeAsync(CapabilityOperations.Calendar.Events,
				new CalendarEventsArguments
				{
					AccountId = accountId, From = query.From, To = query.To, CalendarIds = [.. query.CalendarIds]
				},
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<CalendarEventsResult>(PluginProtocolJson.Options);
		if (result?.Truncated == true)
		{
			_logger.Warning(
				"Plugin {PluginId} truncated the calendar events between {From} and {To}",
				pluginId, query.From, query.To);
		}

		return result is null ? [] : [.. result.Events.Select(CalendarMapper.ToDomain)];
	}

	public async Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Calendar.Event,
				new CalendarEventArguments { AccountId = accountId, CalendarId = calendarId, EventId = eventId },
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<CalendarEventResult>(PluginProtocolJson.Options);
		return result?.Event is { } dto ? CalendarMapper.ToDomain(dto) : null;
	}

	private Task<JsonElement?> InvokeAsync(string operation, object arguments, CancellationToken cancellationToken)
		=> invoker.InvokeAsync(pluginId,
			new CapabilityInvokeRequest
			{
				Kind = CapabilityKinds.Calendar,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				Arguments = arguments
			},
			cancellationToken);
}
