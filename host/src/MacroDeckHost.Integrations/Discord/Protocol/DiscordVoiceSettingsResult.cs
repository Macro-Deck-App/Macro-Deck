namespace MacroDeckHost.Integrations.Discord.Protocol;

internal enum DiscordVoiceSettingsOutcome
{
	Confirmed,
	NotConnected,
	Rejected,
	Failed,
	NotApplied
}

internal enum DiscordFieldMismatch
{
	Omitted,
	Unchanged
}

internal sealed record DiscordUnappliedField(string Field, DiscordFieldMismatch Reason);

internal sealed record DiscordVoiceSettingsResult
{
	public required DiscordVoiceSettingsOutcome Outcome { get; init; }

	public IReadOnlyList<DiscordUnappliedField> UnappliedFields { get; init; } = [];

	public string? Message { get; init; }

	public bool IsConfirmed => Outcome is DiscordVoiceSettingsOutcome.Confirmed;

	public static DiscordVoiceSettingsResult Confirmed()
	{
		return new DiscordVoiceSettingsResult { Outcome = DiscordVoiceSettingsOutcome.Confirmed };
	}

	public static DiscordVoiceSettingsResult NotConnected()
	{
		return new DiscordVoiceSettingsResult { Outcome = DiscordVoiceSettingsOutcome.NotConnected };
	}

	public static DiscordVoiceSettingsResult Rejected(string message)
	{
		return new DiscordVoiceSettingsResult { Outcome = DiscordVoiceSettingsOutcome.Rejected, Message = message };
	}

	public static DiscordVoiceSettingsResult Failed(string message)
	{
		return new DiscordVoiceSettingsResult { Outcome = DiscordVoiceSettingsOutcome.Failed, Message = message };
	}

	public static DiscordVoiceSettingsResult NotApplied(IReadOnlyList<DiscordUnappliedField> unappliedFields)
	{
		return new DiscordVoiceSettingsResult
			{ Outcome = DiscordVoiceSettingsOutcome.NotApplied, UnappliedFields = unappliedFields };
	}
}
