namespace MacroDeck.Sdk.Companion;

public enum CompanionDeviceCommandOutcome
{
	/// <summary>The device confirmed it carried the command out.</summary>
	Done,

	/// <summary>The device accepted the command but did not confirm it in time, so it may or may not have run.</summary>
	Unconfirmed,

	/// <summary>The command did not run; <see cref="CompanionDeviceCommandResult.Failure" /> says why.</summary>
	Failed
}

/// <summary>The answer to <see cref="ICompanionDeviceApi.RequestAsync" />.</summary>
public sealed record CompanionDeviceCommandResult
{
	private CompanionDeviceCommandResult(CompanionDeviceCommandOutcome outcome, CompanionDeviceFailure? failure)
	{
		Outcome = outcome;
		Failure = failure;
	}

	public CompanionDeviceCommandOutcome Outcome { get; }

	/// <summary>Set only when <see cref="Outcome" /> is <see cref="CompanionDeviceCommandOutcome.Failed" />.</summary>
	public CompanionDeviceFailure? Failure { get; }

	public static CompanionDeviceCommandResult Done { get; } = new(CompanionDeviceCommandOutcome.Done, null);

	public static CompanionDeviceCommandResult Unconfirmed { get; }
		= new(CompanionDeviceCommandOutcome.Unconfirmed, null);

	public static CompanionDeviceCommandResult Failed(CompanionDeviceFailure failure)
	{
		return new CompanionDeviceCommandResult(CompanionDeviceCommandOutcome.Failed, failure);
	}
}
