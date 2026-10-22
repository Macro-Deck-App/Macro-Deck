using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;

namespace MacroDeckHost.Application.Integrations;

public static class IntegrationCapabilityFingerprints
{
	public static IReadOnlyDictionary<string, string> Batteries(IReadOnlyList<BatteryDeviceSnapshot> devices)
	{
		return devices
			.GroupBy(device => device.ProviderId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => string.Join('\n', group.Select(device => device.ToString())),
				StringComparer.Ordinal);
	}

	public static IReadOnlyDictionary<string, string> Calendars(CalendarSnapshot snapshot)
	{
		var accounts = snapshot.Accounts
			.Select(state => (state.Account.IntegrationId,
				Line: $"{state.Account}|{state.Status}|{string.Join(';', state.Calendars)}"));
		var events = snapshot.Events.Select(summary => (summary.IntegrationId, Line: summary.ToString()));

		return accounts.Concat(events)
			.GroupBy(entry => entry.IntegrationId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => string.Join('\n', group.Select(entry => entry.Line)),
				StringComparer.Ordinal);
	}

	public static IReadOnlyDictionary<string, string> EmailInboxes(EmailInboxesSnapshot snapshot)
	{
		return snapshot.Inboxes
			.GroupBy(state => state.Inbox.IntegrationId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => string.Join('\n', group.Select(Describe)),
				StringComparer.Ordinal);
	}

	public static IReadOnlyList<string> ChangedOwners(
		IReadOnlyDictionary<string, string> previous,
		IReadOnlyDictionary<string, string> next)
	{
		return
		[
			.. previous.Keys
				.Union(next.Keys, StringComparer.Ordinal)
				.Where(owner => previous.GetValueOrDefault(owner) != next.GetValueOrDefault(owner))
				.Order(StringComparer.Ordinal)
		];
	}

	private static string Describe(EmailInboxState state)
	{
		return $"{state.Inbox}|{state.Status}|{state.StatusMessage}|{state.UnreadCount}|{state.TotalCount}|"
			+ $"{state.SyncedAt:O}|{state.IsStale}|{string.Join(';', state.Latest)}";
	}
}
