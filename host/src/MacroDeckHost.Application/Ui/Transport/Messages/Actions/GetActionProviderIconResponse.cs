namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ActionProviderIconReferenceDto
{
	public string Type { get; set; } = string.Empty;

	public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// The provider's answer to the editor probe, kept as three distinguishable outcomes rather than
/// collapsed into one (issue #425 decision 9): <see cref="HasSnapshot" /> false means the provider itself
/// answered <c>null</c> ("cannot answer right now") - a valid, non-error response; a snapshot with
/// <see cref="NoIcon" /> set is deliberately blank; otherwise <see cref="Reference" /> (when the provider
/// named one) or <see cref="MediaType" /> (when it supplies bytes instead) describes what it would show.
/// A transport-level failure (the action does not exist, is not an icon provider, or timed out) is
/// reported through <see cref="Error" /> instead, never collapsed into "no snapshot".
/// </summary>
public class GetActionProviderIconResponse
{
	public bool HasSnapshot { get; set; }

	public string Version { get; set; } = string.Empty;

	public ActionProviderIconReferenceDto? Reference { get; set; }

	public string? MediaType { get; set; }

	public bool NoIcon { get; set; }

	public TransportError? Error { get; set; }
}
