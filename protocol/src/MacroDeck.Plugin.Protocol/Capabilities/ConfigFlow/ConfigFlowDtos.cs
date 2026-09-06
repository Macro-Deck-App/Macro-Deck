using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;

/// <summary>The full result of the <c>config-flow</c> capability's <c>describe</c> operation - just the
/// one flag <c>RemotePluginSnapshotRefresher</c> folds into the snapshot, since a config flow has no
/// catalogue to describe ahead of a session (unlike <c>weather</c>/<c>music-player</c>, it declares no
/// instances).</summary>
public sealed record ConfigFlowDescribePayload
{
	public required bool AllowsMultipleConfigurations { get; init; }

	/// <summary>
	/// Whether the provider implements <c>IUiConfigFlowProvider</c> and its flows can render themselves
	/// as a Macro Deck UI tree. The declared step is still served either way: a client that cannot
	/// render a tree walks the ordinary <see cref="ConfigFlowStepDto" /> path, and the step's fields
	/// remain how the host learns which submitted values are secret. Additive within protocol v1; never
	/// gated on the negotiated version, so a plugin that predates it sends nothing and keeps the
	/// historic behaviour of offering no tree.
	/// </summary>
	public bool ServesConfigUiTree { get; init; }
}

/// <summary>Mirrors the SDK's <c>ConfigFlowCopyValue</c>.</summary>
public sealed record ConfigFlowCopyValueDto
{
	public required LocalizedText Label { get; init; }

	public required string Value { get; init; }
}

/// <summary>Mirrors the SDK's <c>ConfigFlowInstruction</c>.</summary>
public sealed record ConfigFlowInstructionDto
{
	public required LocalizedText Text { get; init; }

	public IReadOnlyList<ConfigFlowCopyValueDto> Values { get; init; } = [];
}

/// <summary>Mirrors the SDK's <c>ConfigFlowLink</c>.</summary>
public sealed record ConfigFlowLinkDto
{
	public required LocalizedText Label { get; init; }

	public required string Url { get; init; }
}

/// <summary>Mirrors the SDK's <c>ConfigFlowStep</c>. <see cref="Fields" />/<see cref="AdvancedFields" />
/// reuse <see cref="ActionParameterDto" /> exactly as the SDK type reuses <c>ActionParameter</c>.</summary>
public sealed record ConfigFlowStepDto
{
	public required string StepId { get; init; }

	public LocalizedText? Title { get; init; }

	public LocalizedText? Description { get; init; }

	public IReadOnlyList<ConfigFlowCopyValueDto> Values { get; init; } = [];

	public IReadOnlyList<ConfigFlowInstructionDto> Instructions { get; init; } = [];

	public IReadOnlyList<ConfigFlowLinkDto> Links { get; init; } = [];

	public required IReadOnlyList<ActionParameterDto> Fields { get; init; }

	public IReadOnlyList<ActionParameterDto> AdvancedFields { get; init; } = [];
}

/// <summary>Mirrors the SDK's <c>ConfigFlowValue</c>.</summary>
public sealed record ConfigFlowValueDto
{
	public string? Value { get; init; }

	public bool IsSecret { get; init; }
}

/// <summary>
/// Mirrors the SDK's <c>ConfigFlowResult</c>. <see cref="Kind" /> is a string, not the SDK's
/// <c>ConfigFlowResultKind</c> enum - see <c>ActionParameterDto</c>'s remarks. <c>ConfigFlowResult</c>
/// has a private constructor and only exposes its four shapes through static factories, so it is not
/// round-trippable on its own; this DTO carries every field any of the four factories can set, and the
/// mapper on each side picks which ones are meaningful for a given <see cref="Kind" />.
/// </summary>
public sealed record ConfigFlowResultDto
{
	/// <summary>One of the SDK's <c>ConfigFlowResultKind</c> member names: "Step", "Error", "Complete", "External".</summary>
	public required string Kind { get; init; }

	/// <summary>Set for Step and Error.</summary>
	public ConfigFlowStepDto? NextStep { get; init; }

	/// <summary>Set for Error only.</summary>
	public LocalizedText? ErrorMessage { get; init; }

	/// <summary>Set for Error only.</summary>
	public IReadOnlyDictionary<string, LocalizedText>? FieldErrors { get; init; }

	/// <summary>Set for Complete only. Plain text, not localized: the host stores it as the configured
	/// entry's name, which the user then owns - a name that changed language under them would be a
	/// different bug from a label that does.</summary>
	public string? EntryTitle { get; init; }

	/// <summary>Set for Complete only.</summary>
	public IReadOnlyDictionary<string, ConfigFlowValueDto>? Values { get; init; }

	/// <summary>Set for External only.</summary>
	public string? ExternalUrl { get; init; }

	/// <summary>Set for External only.</summary>
	public string? ResumeStepId { get; init; }
}

/// <summary>
/// The OAuth context of one <c>flow.start</c>/<c>flow.submit</c> call, mirroring
/// <c>IOAuthSession</c>'s three members. Sent as plain arguments on every call rather than modelled as a
/// live session object on the wire: <c>IOAuthSession.AuthorizationCode</c> is populated by the host
/// between calls, and there is no wire concept of a property a remote flow could re-read mid-step - see
/// <c>RemoteConfigFlow</c>'s remarks on the resulting (small, documented) divergence from the in-process
/// contract.
/// </summary>
public sealed record ConfigFlowOAuthContextDto
{
	public required string RedirectUri { get; init; }

	public required string State { get; init; }

	public string? AuthorizationCode { get; init; }
}

/// <summary>
/// Arguments for the <c>flow.start</c> operation. <see cref="SessionId" /> is minted host-side once per
/// <c>IConfigFlowProvider.CreateConfigFlow</c> call and carried on every subsequent
/// <c>flow.submit</c>/<c>flow.abandon</c> for the same session - see <c>ConfigFlowCapabilityHandler</c>'s
/// remarks on why a plugin-side session map, not the wire, is what makes <c>IConfigFlow</c>'s
/// instance-per-session contract work across a socket.
/// </summary>
public sealed record FlowStartArguments
{
	public required string SessionId { get; init; }

	public required ConfigFlowOAuthContextDto OAuth { get; init; }

	/// <summary>
	/// The existing or host-requested entry title. Null means the flow chooses the title for a new entry.
	/// </summary>
	public string? EntryTitle { get; init; }
}

/// <summary>
/// Arguments for the <c>flow.submit</c> operation. <see cref="Input" /> values are already resolved to
/// plaintext by the host, exactly as <c>IConfigFlow.SubmitAsync</c>'s own doc comment requires - the
/// plugin never sees a secret reference here, only what the user typed (or the host decrypted).
/// </summary>
public sealed record FlowSubmitArguments
{
	public required string SessionId { get; init; }

	public required string StepId { get; init; }

	public required IReadOnlyDictionary<string, JsonElement> Input { get; init; }

	public required ConfigFlowOAuthContextDto OAuth { get; init; }

	/// <inheritdoc cref="FlowStartArguments.EntryTitle"/>
	public string? EntryTitle { get; init; }
}

/// <summary>Arguments for the <c>flow.abandon</c> operation: which plugin-side session to release.</summary>
public sealed record FlowAbandonArguments
{
	public required string SessionId { get; init; }
}
