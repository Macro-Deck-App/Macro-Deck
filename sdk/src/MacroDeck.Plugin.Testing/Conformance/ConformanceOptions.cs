namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>Configures which checks a <see cref="ConformanceRunner" /> runs, and how long each gets.</summary>
public sealed class ConformanceOptions
{
	/// <summary>Restricts the run to checks in one of these categories. Empty (the default) means every category.</summary>
	public IReadOnlyCollection<ConformanceCategory> Categories { get; init; } = [];

	/// <summary>
	/// Restricts the run to checks whose <see cref="IConformanceCheck.Id" /> is in this set. Empty (the
	/// default) means every id. Combined with <see cref="Categories" /> and <see cref="RequiredOnly" /> as an
	/// intersection when more than one is set.
	/// </summary>
	public IReadOnlyCollection<string> Ids { get; init; } = [];

	/// <summary>When true, only <see cref="ConformanceRequirement.Required" /> checks run. Default false.</summary>
	public bool RequiredOnly { get; init; }

	/// <summary>
	/// How long a single check's <see cref="IConformanceCheck.RunAsync" /> gets before <see cref="ConformanceRunner" />
	/// gives up on it and records <see cref="ConformanceOutcome.Inconclusive" /> - enforced as a real
	/// wall-clock bound independent of whether the check itself ever observes the <see cref="CancellationToken" />
	/// it was given, so a check that ignores cancellation entirely still cannot stall the run past this
	/// budget. Defaults to 60 seconds: several checks (disconnect/reconnect in particular) chain more than
	/// one bounded wait against a real process, possibly one this run's default host had to launch itself -
	/// short enough that one hung check still cannot silently stall an entire run.
	/// </summary>
	public TimeSpan PerCheckTimeout { get; init; } = TimeSpan.FromSeconds(60);

	/// <summary>Whether <paramref name="check" /> is selected by this configuration.</summary>
	internal bool Selects(IConformanceCheck check)
	{
		ArgumentNullException.ThrowIfNull(check);

		if (RequiredOnly && check.Requirement != ConformanceRequirement.Required)
		{
			return false;
		}

		if (Categories.Count > 0 && !Categories.Contains(check.Category))
		{
			return false;
		}

		return Ids.Count == 0 || Ids.Contains(check.Id);
	}
}
