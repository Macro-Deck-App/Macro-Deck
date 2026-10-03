using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck;

internal static class DeckDeviceTarget
{
	public const string ParameterName = "deviceId";

	public static ActionParameter Parameter() => ActionParameter.DynamicChoice(ParameterName,
		label: AppStrings.Integrations.Deck.Actions.DeviceLabel(),
		description: AppStrings.Integrations.Deck.Actions.DeviceDescription(),
		optionsSourceId: DeckOptionsSourceIds.Devices,
		placeholder: AppStrings.Integrations.Deck.Actions.DevicePlaceholder());

	// A chosen device wins over the press origin, and an offline one fails instead of falling back to everyone.
	public static bool TryResolve(IDeckNavigator navigator,
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
				AppStrings.Integrations.Deck.Errors.DeviceNotConnected(deviceId: raw));
			return false;
		}

		originClientId = client.ClientId;
		return true;
	}
}
