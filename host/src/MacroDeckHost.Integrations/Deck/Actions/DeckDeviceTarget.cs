using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck.Actions;

internal static class DeckDeviceTarget
{
	private const string ParameterName = "deviceId";

	public static ActionParameter Parameter()
	{
		return ActionParameter.DynamicChoice(ParameterName,
			AppStrings.Integrations.Deck.Actions.DeviceLabel(),
			AppStrings.Integrations.Deck.Actions.DeviceDescription(),
			HostOptionsSources.Devices,
			AppStrings.Integrations.Deck.Actions.DevicePlaceholder());
	}

	// A chosen device wins over the press origin, and an offline one fails instead of falling back to everyone.
	public static bool TryResolve(
		IDeckNavigator navigator,
		ActionExecutionContext context,
		out string? originClientId,
		out ActionResult? failure)
	{
		failure = null;
		originClientId = context.OriginClientId;

		var raw = context.Parameters.TryGetValue(ParameterName, out var value) ? value?.ToString() : null;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return true;
		}

		var client = Guid.TryParse(raw, out var deviceId)
			? navigator.GetClients().FirstOrDefault(c => Guid.TryParse(c.DeviceId, out var id) && id == deviceId)
			: null;
		if (client is null)
		{
			failure = ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.Deck.Errors.DeviceNotConnected(raw));
			return false;
		}

		originClientId = client.ClientId;
		return true;
	}
}
