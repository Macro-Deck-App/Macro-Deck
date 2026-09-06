using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal static class DiscordVoiceActionResults
{
	private const string RemedyHint =
		"Another application connected over Discord's RPC interface may hold the voice settings lock, or " +
		"this Discord client does not offer the setting.";

	public static ActionResult ToActionResult(DiscordVoiceSettingsResult result, string action)
	{
		try
		{
			ThrowIfNotApplied(result, action);
			return ActionResult.Success();
		}
		catch (DiscordVoiceActionException ex)
		{
			var code = result.Outcome switch
			{
				DiscordVoiceSettingsOutcome.NotConnected => ActionErrorCodes.NotConnected,
				DiscordVoiceSettingsOutcome.Rejected or DiscordVoiceSettingsOutcome.NotApplied =>
					ActionErrorCodes.ProviderRejected,
				_ => ActionErrorCodes.ProviderError
			};

			return ActionResult.Failed(code,
				AppStrings.Integrations.Discord.Errors.VoiceActionFailed(details: ex.Message));
		}
	}

	public static void ThrowIfNotApplied(DiscordVoiceSettingsResult result, string action)
	{
		switch (result.Outcome)
		{
			case DiscordVoiceSettingsOutcome.Confirmed:
				return;

			case DiscordVoiceSettingsOutcome.NotConnected:
				throw new DiscordVoiceActionException($"Discord is not running, so \"{action}\" did nothing.");

			case DiscordVoiceSettingsOutcome.Rejected:
				throw new DiscordVoiceActionException($"Discord rejected \"{action}\": {result.Message}");

			case DiscordVoiceSettingsOutcome.Failed:
				throw new DiscordVoiceActionException($"Discord could not apply \"{action}\": {result.Message}");

			case DiscordVoiceSettingsOutcome.NotApplied:
				throw new DiscordVoiceActionException(FormatNotApplied(result.UnappliedFields, action));

			default:
				throw new DiscordVoiceActionException($"Discord did not confirm \"{action}\".");
		}
	}

	private static string FormatNotApplied(IReadOnlyList<DiscordUnappliedField> fields, string action)
	{
		var omitted = fields.Where(f => f.Reason == DiscordFieldMismatch.Omitted).Select(f => f.Field).ToList();
		var unchanged = fields.Where(f => f.Reason == DiscordFieldMismatch.Unchanged).Select(f => f.Field).ToList();

		var clauses = new List<string>();
		if (omitted.Count > 0)
		{
			clauses.Add($"did not report {string.Join(", ", omitted)} back");
		}

		if (unchanged.Count > 0)
		{
			clauses.Add($"kept {string.Join(", ", unchanged)} unchanged");
		}

		return $"Discord did not apply \"{action}\": it {string.Join(" and ", clauses)}. {RemedyHint}";
	}
}
