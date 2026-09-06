using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.Actions;

/// <summary>
/// One action a plugin declares, served by the <c>actions</c> capability's <c>describe</c> operation.
/// <see cref="LocalId" /> is the same id the action was declared under at handshake - the host indexes
/// the catalogue by it, so this is not repeated per-item metadata but the join key back to
/// <c>DeclaredCapability</c>.
/// </summary>
public sealed record ActionDescriptorDto
{
	public required string LocalId { get; init; }

	public required LocalizedText Name { get; init; }

	public required LocalizedText Description { get; init; }

	public required IReadOnlyList<ActionParameterDto> Parameters { get; init; }

	/// <summary>Non-null when the action implements <c>IConfigurableActionDefinition</c>.</summary>
	public string? DescriptiveUiSchema { get; init; }

	/// <summary>Whether the action implements <c>IDynamicOptionsActionDefinition</c>.</summary>
	public bool SupportsDynamicOptions { get; init; }

	/// <summary>
	/// Whether the action implements <c>IStateProviderActionDefinition</c> - presence alone is what the
	/// host's adapter factory keys on when picking which leaf to build. Additive within protocol v1;
	/// never gated on the negotiated version.
	/// </summary>
	public bool ProvidesState { get; init; }

	/// <summary>
	/// Whether the action implements <c>IUiConfigurableActionDefinition</c> and can render its
	/// configuration as a Macro Deck UI tree. <see cref="Parameters" /> stays authoritative either way:
	/// a client that cannot render a tree configures the action through the declared list, unchanged.
	/// Additive within protocol v1; never gated on the negotiated version, so a plugin that predates it
	/// sends nothing and keeps the historic behaviour of offering no tree.
	/// </summary>
	public bool ConfiguresWithUiTree { get; init; }

	/// <summary>
	/// Whether the action implements <c>IIconProviderActionDefinition</c> - presence alone is what the
	/// host's standalone icon-provider registry keys on, since the closed remote-adapter family
	/// (see <c>RemoteActionDefinitionFactory</c>) does not grow a fifth leaf for it. Additive within
	/// protocol v1; never gated on the negotiated version.
	/// </summary>
	public bool ProvidesIcon { get; init; }
}

/// <summary>The full result of the <c>actions</c> capability's <c>describe</c> operation.</summary>
public sealed record ActionCatalogPayload
{
	public required IReadOnlyList<ActionDescriptorDto> Actions { get; init; }
}

/// <summary>Arguments for the <c>execute</c> operation, mirroring <c>ActionExecutionContext</c> minus
/// the parts that have no wire representation (the interaction channel, the cancellation token).</summary>
public sealed record ActionExecuteArguments
{
	public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Parameters { get; init; }
		= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

	public string? OriginClientId { get; init; }

	public string? OwnerWidgetId { get; init; }
}

/// <summary>Result of the <c>execute</c> operation. A failure is carried as a
/// <c>CAPABILITY_UNSUPPORTED</c>/provider-specific <c>protocol.error</c> instead, per
/// <c>ActionsCapabilityHandler.Map</c> - this DTO carries only successful or accepted results.</summary>
public sealed record ActionExecuteResult
{
	/// <summary>True when the provider took the request but has not confirmed it (<c>ActionResultStatus.Accepted</c>).</summary>
	public bool Accepted { get; init; }

	public LocalizedText? Message { get; init; }

	/// <summary>Optional state the host may render until the state provider confirms it.</summary>
	public string? ExpectedStateId { get; init; }
}

/// <summary>Arguments for the <c>options</c> operation, mirroring <c>DynamicOptionsContext</c>.</summary>
public sealed record DynamicOptionsArguments
{
	public required string ParameterName { get; init; }

	public string? Filter { get; init; }

	public IReadOnlyDictionary<string, System.Text.Json.JsonElement> CurrentParameters { get; init; }
		= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
}

/// <summary>Result of the <c>options</c> operation, mirroring <c>DynamicOptionsResult</c>.</summary>
public sealed record DynamicOptionsResultDto
{
	public required IReadOnlyList<ActionParameterOptionDto> Options { get; init; }

	public bool AllowsCustomValue { get; init; }

	public int? CacheSeconds { get; init; }

	/// <summary>
	/// Mirrors <c>DynamicOptionsResult.Error</c>. Additive and optional: absent on the wire means "no
	/// error", which is exactly how a plugin built before this field existed still serializes and how a
	/// host built before it existed still reads.
	/// </summary>
	public LocalizedText? Error { get; init; }
}

/// <summary>Initial appearance of one state, mirroring <c>MacroDeck.Sdk.Actions.ActionStateAppearance</c>.
/// Every field is optional; the host applies them only to a state a button is adopting for the first time.</summary>
public sealed record ActionStateAppearanceDto
{
	public string? Label { get; init; }

	public string? BackgroundColor { get; init; }

	public string? LabelColor { get; init; }

	public string? IconId { get; init; }
}

/// <summary>One state of a <c>state</c> operation reply, mirroring <c>MacroDeck.Sdk.Actions.ActionStateDefinition</c>.</summary>
public sealed record ActionStateDefinitionDto
{
	public required string Id { get; init; }

	public required LocalizedText Label { get; init; }

	public ActionStateAppearanceDto? DefaultAppearance { get; init; }
}

/// <summary>Result of the <c>state</c> operation. Null fields (not a null payload) represent
/// <c>IStateProviderActionDefinition.GetActionStateAsync</c> returning <c>null</c> - see <see cref="HasValue" />.</summary>
public sealed record ActionStateResult
{
	public bool HasValue { get; init; }

	public IReadOnlyList<ActionStateDefinitionDto> States { get; init; } = [];

	public string? ActiveStateId { get; init; }
}

/// <summary>One icon reference for the <c>icon</c> operation, mirroring
/// <c>MacroDeck.Sdk.Actions.ActionIconReference</c>.</summary>
public sealed record ActionIconReferenceDto
{
	public required string Type { get; init; }

	public required string Reference { get; init; }
}

/// <summary>Result of the <c>icon</c> operation. Null fields (not a null payload) represent
/// <c>IIconProviderActionDefinition.GetActionIconAsync</c> returning <c>null</c> - see <see cref="HasValue" />.</summary>
public sealed record ActionIconResult
{
	public bool HasValue { get; init; }

	public string Version { get; init; } = string.Empty;

	public ActionIconReferenceDto? Reference { get; init; }

	public string? MediaType { get; init; }

	public bool NoIcon { get; init; }
}

/// <summary>Arguments for the <c>icon.content</c> operation, mirroring
/// <c>IIconProviderActionDefinition.GetActionIconContentAsync</c>.</summary>
public sealed record ActionIconContentArguments
{
	public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Parameters { get; init; }
		= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

	public required string Version { get; init; }
}

/// <summary>Result of the <c>icon.content</c> operation. Null fields (not a null payload) represent
/// <c>IIconProviderActionDefinition.GetActionIconContentAsync</c> returning <c>null</c> - see
/// <see cref="HasValue" />.</summary>
public sealed record ActionIconContentResult
{
	public bool HasValue { get; init; }

	public string? ContentHash { get; init; }

	public string? MediaType { get; init; }
}
