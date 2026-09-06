using MacroDeck.Localization;

namespace MacroDeckHost.Application.ClientTargets;

/// <summary>A value the user has to read or copy, e.g. the address to point a device at.</summary>
public sealed record WebClientTargetProvisioningValue(LocalizedText Label, string Value);

/// <summary>A link a step points at, e.g. the tool that prepares the device.</summary>
public sealed record WebClientTargetProvisioningLink(LocalizedText Label, string Url);

/// <summary>An editable value on a step, pre-filled with the documented default for the device.</summary>
public sealed record WebClientTargetProvisioningField(
	string FieldId,
	LocalizedText Label,
	LocalizedText Description,
	string DefaultValue,
	IReadOnlyList<WebClientTargetProvisioningChoice>? Choices = null);

/// <summary>One option of a field the user picks from rather than types.</summary>
public sealed record WebClientTargetProvisioningChoice(string Value, LocalizedText Label);

/// <summary>
/// One screen of a target's provisioning walkthrough (issue #727). Deliberately shaped like
/// <c>ConfigFlowStep</c> - an ordered instruction list above the values a user needs - because that
/// is the shape a device setup guide takes, but it is its own type: provisioning a device produces no
/// config entry, so it must not borrow the semantics of one.
/// </summary>
public sealed class WebClientTargetProvisioningStep
{
	public required string StepId { get; init; }

	public LocalizedText Title { get; init; }

	/// <summary>The sentence introducing <see cref="Instructions"/>, or the whole step if there are none.</summary>
	public LocalizedText Description { get; init; }

	/// <summary>Ordered manual steps, rendered as a numbered list.</summary>
	public IReadOnlyList<LocalizedText> Instructions { get; init; } = [];

	public IReadOnlyList<WebClientTargetProvisioningValue> Values { get; init; } = [];

	public IReadOnlyList<WebClientTargetProvisioningLink> Links { get; init; } = [];

	/// <summary>
	/// Values the user may correct before the step runs. This is how a target stays usable across
	/// firmware variants without a preference family per device: the documented default is filled in,
	/// and a device that keeps its kiosk configuration somewhere else is an edit here.
	/// </summary>
	public IReadOnlyList<WebClientTargetProvisioningField> Fields { get; init; } = [];

	/// <summary>
	/// Whether the user can advance from here. False while the step is waiting on something only they
	/// can do - plugging the device in, running the flashing tool - so the UI offers a retry instead.
	/// </summary>
	public bool CanContinue { get; init; } = true;
}
