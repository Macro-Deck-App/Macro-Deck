using MacroDeck.Localization;

namespace MacroDeck.Sdk.ConfigFlow;

public enum ConfigFlowResultKind
{
	Step,
	Error,
	Complete,
	External
}

/// <summary>
/// Outcome of a config-flow step. Build one with <see cref="Step"/>, <see cref="Error"/>,
/// <see cref="External"/> or <see cref="Complete"/>.
/// </summary>
public sealed class ConfigFlowResult
{
	private ConfigFlowResult(ConfigFlowResultKind kind)
	{
		Kind = kind;
	}

	public ConfigFlowResultKind Kind { get; }

	/// <summary>The step to display – set for <see cref="ConfigFlowResultKind.Step"/> and <see cref="ConfigFlowResultKind.Error"/>.</summary>
	public ConfigFlowStep? NextStep { get; private init; }

	/// <summary>Optional general error message shown above the form (Error only).</summary>
	public LocalizedText ErrorMessage { get; private init; }

	/// <summary>Optional per-field error messages keyed by field name (Error only).</summary>
	public IReadOnlyDictionary<string, LocalizedText>? FieldErrors { get; private init; }

	/// <summary>The title of the config entry to persist (Complete only).</summary>
	public string? EntryTitle { get; private init; }

	/// <summary>Extra values to persist alongside the collected form fields (Complete only).</summary>
	public IReadOnlyDictionary<string, ConfigFlowValue>? Values { get; private init; }

	/// <summary>External authorization URL to open in the browser (External only).</summary>
	public string? ExternalUrl { get; private init; }

	/// <summary>Step id to auto-submit once the external callback arrives (External only).</summary>
	public string? ResumeStepId { get; private init; }

	/// <summary>Whether completing stores a config entry (Complete only): <c>false</c> for a result of
	/// <see cref="CompleteWithoutEntry" />.</summary>
	public bool StoresEntry { get; private init; } = true;

	/// <summary>Show the next (or first) step.</summary>
	public static ConfigFlowResult Step(ConfigFlowStep step)
	{
		return new ConfigFlowResult(ConfigFlowResultKind.Step) { NextStep = step };
	}

	/// <summary>Re-display <paramref name="step"/> with validation errors.</summary>
	public static ConfigFlowResult Error(
		ConfigFlowStep step,
		LocalizedText message = default,
		IReadOnlyDictionary<string, LocalizedText>? fieldErrors = null)
	{
		return new ConfigFlowResult(ConfigFlowResultKind.Error)
		{
			NextStep = step,
			ErrorMessage = message,
			FieldErrors = fieldErrors
		};
	}

	/// <summary>
	/// Hand the user off to an external authorization URL (e.g. an OAuth consent page). The host
	/// opens it, waits for the redirect callback, then auto-submits <paramref name="resumeStepId"/>.
	/// </summary>
	public static ConfigFlowResult External(string url, string resumeStepId)
	{
		return new ConfigFlowResult(ConfigFlowResultKind.External) { ExternalUrl = url, ResumeStepId = resumeStepId };
	}

	/// <summary>
	/// Finish the flow; the host persists the collected form values plus any extra
	/// <paramref name="values"/> (e.g. OAuth tokens) as a config entry.
	/// </summary>
	public static ConfigFlowResult Complete(
		string title,
		IReadOnlyDictionary<string, ConfigFlowValue>? values = null)
	{
		return new ConfigFlowResult(ConfigFlowResultKind.Complete) { EntryTitle = title, Values = values };
	}

	/// <summary>
	/// Finish the flow without storing anything: the dialog closes, no config entry is created or changed and
	/// the collected values are discarded. For an integration that creates its entries itself, for example
	/// when a device connects. When the user switched the integration off and it has no usable entry, that
	/// stored choice is removed.
	/// </summary>
	/// <remarks>A Macro Deck that predates it treats the result like <see cref="Complete" /> without a title:
	/// it stores an entry from the collected values under the integration's name.</remarks>
	public static ConfigFlowResult CompleteWithoutEntry()
	{
		return new ConfigFlowResult(ConfigFlowResultKind.Complete) { StoresEntry = false };
	}
}
