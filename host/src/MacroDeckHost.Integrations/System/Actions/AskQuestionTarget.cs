using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Integrations.System.Actions;

internal static class AskQuestionTarget
{
	public const string Origin = "origin";
	public const string Device = "device";
	public const string All = "all";

	public static NotificationTarget? Resolve(
		IDeckNavigator navigator,
		string target,
		string? originClientId,
		string? deviceId)
	{
		var clients = navigator.GetClients().Where(client => !client.IsDeviceSession).ToList();

		return target switch
		{
			Device => ResolveDevice(clients, deviceId),
			All => clients.Count > 0 ? NotificationTarget.All : null,
			_ => ResolveOrigin(clients, originClientId)
		};
	}

	private static NotificationTarget? ResolveOrigin(IReadOnlyList<DeckClient> clients, string? originClientId)
	{
		var client = clients.FirstOrDefault(candidate => candidate.ClientId == originClientId);
		if (client is null)
		{
			return null;
		}

		return Guid.TryParse(client.DeviceId, out var id) ? NotificationTarget.Devices(id) : NotificationTarget.AdminUi;
	}

	private static NotificationTarget? ResolveDevice(IReadOnlyList<DeckClient> clients, string? deviceId)
	{
		if (!Guid.TryParse(deviceId, out var id))
		{
			return null;
		}

		return clients.Any(client => Guid.TryParse(client.DeviceId, out var candidate) && candidate == id)
			? NotificationTarget.Devices(id)
			: null;
	}
}
