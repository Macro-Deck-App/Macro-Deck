namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>What <see cref="PluginScaffoldWizard.Run" /> concluded: a validation failure (usage error), a
/// declined confirmation (cancelled, no error line), or a ready-to-scaffold request. Cancellation via the
/// token instead propagates as <see cref="OperationCanceledException" />, which <c>CliEntryPoint</c>
/// already maps to <see cref="ExitCode.Cancelled" /> for every command.</summary>
internal sealed record PluginWizardOutcome
{
	public bool Declined { get; init; }

	public CliDiagnostic? Error { get; init; }

	public PluginScaffoldRequest? Request { get; init; }

	public static PluginWizardOutcome Decline() => new() { Declined = true };

	public static PluginWizardOutcome Invalid(CliDiagnostic error) => new() { Error = error };

	public static PluginWizardOutcome Accept(PluginScaffoldRequest request) => new() { Request = request };
}
