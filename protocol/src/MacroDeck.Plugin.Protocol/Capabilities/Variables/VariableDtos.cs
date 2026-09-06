using System.Text.Json.Serialization;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.Variables;

/// <summary>
/// Mirrors the SDK's <c>VariableDefinition</c> - one variable, eager or on-demand.
/// <see cref="Type" /> and <see cref="Materialization" /> are strings, not the SDK's enums - see
/// <see cref="MacroDeck.Plugin.Protocol.Capabilities.Actions.ActionParameterDto" />'s remarks for why
/// every wire DTO in this project follows that rule.
/// </summary>
public sealed record VariableDefinitionDto
{
	/// <summary>The provider's own stable identity for this variable. Required for an on-demand
	/// definition; null on an eager one whose provider never set one, and the reader then derives one
	/// from <see cref="Name" />.</summary>
	public string? Id { get; init; }

	/// <summary>The canonical variable name for an eager definition, or the name to suggest at bind time
	/// for an on-demand one. Null when an on-demand definition leaves the naming to the reader.</summary>
	public string? Name { get; init; }

	/// <summary>One of the SDK's <c>VariableType</c> member names, e.g. "Text", "Numeric", "Boolean".</summary>
	public required string Type { get; init; }

	/// <summary>
	/// <c>"eager"</c> or <c>"on-demand"</c>. Not <c>required</c>, and defaults to <c>"eager"</c>: this
	/// member postdates the snapshots the host persists verbatim, and a document written before it
	/// existed carried only eager variables.
	/// </summary>
	public string Materialization { get; init; } = VariableMaterializations.Eager;

	/// <summary>Null (or empty) when the provider never set one, or when it was produced by a plugin
	/// built before this existed; the reader then falls back to <see cref="Name" />.</summary>
	public LocalizedText? DisplayName { get; init; }

	public LocalizedText? Description { get; init; }

	public string? Icon { get; init; }

	public int? DecimalPlaces { get; init; }

	public double? RefreshIntervalSeconds { get; init; }

	/// <summary>The unit symbol the value is expressed in - <c>%</c>, <c>GB</c>, <c>dB</c>. A plain
	/// string, because a unit symbol is notation rather than prose.</summary>
	public string? Unit { get; init; }

	/// <summary>One of the SDK's <c>VariableSemanticKinds</c> values, or any other string. An open
	/// vocabulary: a reader that does not recognise it renders a plain number with the unit.</summary>
	public string? SemanticKind { get; init; }

	/// <summary>Provider-defined attributes the reader forwards without interpreting. Bounded by the
	/// SDK's <c>VariableLimits</c>; entries beyond those bounds are dropped, not rejected.</summary>
	public IReadOnlyDictionary<string, string>? Attributes { get; init; }

	public string? ParentId { get; init; }

	public bool IsContainer { get; init; }

	public bool IsBindable { get; init; } = true;

	/// <summary>Mirrors <c>VariableConfiguration.Key</c>. Null when the variable has no configured
	/// instance, or when produced by a plugin built before this existed.</summary>
	public string? ConfigurationKey { get; init; }

	/// <summary>Mirrors <c>VariableConfiguration.Name</c>. Null (or empty) alongside a null
	/// <see cref="ConfigurationKey" />.</summary>
	public LocalizedText? ConfigurationName { get; init; }

	/// <summary>Non-null when the owner accepts <c>set</c> for this variable. Presence alone is what a
	/// client keys on when deciding whether to offer an editing control.</summary>
	public VariableWriteCapabilityDto? Write { get; init; }
}

/// <summary>The <c>materialization</c> vocabulary of <see cref="VariableDefinitionDto" />.</summary>
public static class VariableMaterializations
{
	public const string Eager = "eager";

	public const string OnDemand = "on-demand";
}

/// <summary>Mirrors the SDK's <c>VariableWriteCapability</c>.</summary>
public sealed record VariableWriteCapabilityDto
{
	/// <summary>Whether a continuous control should send the value once on release instead of streaming
	/// it during a drag.</summary>
	public bool CommitOnRelease { get; init; }
}

/// <summary>The full result of the <c>variables</c> capability's <c>describe</c> operation - the
/// provider's eager lists plus the flags describing its catalog half.</summary>
public sealed record VariableCatalogPayload
{
	/// <summary>
	/// The eager variables the provider currently supplies. Not <c>required</c>: the host persists this
	/// payload verbatim in its capability snapshot, and a document written before the member was renamed
	/// has to keep deserializing rather than quarantining every plugin's cached capabilities with it.
	/// </summary>
	public IReadOnlyList<VariableDefinitionDto> Variables { get; init; } = [];

	/// <summary>The eager variables the provider declares before it is configured. Not <c>required</c>,
	/// for the same reason as <see cref="Variables" />.</summary>
	public IReadOnlyList<VariableDefinitionDto> DeclaredVariables { get; init; } = [];

	public bool VariablesDependOnConfiguration { get; init; }

	/// <summary>Whether the provider offers a browsable catalog beyond <see cref="Variables" />. False
	/// means <c>discover</c>, <c>resolve</c> and <c>subscribe</c> are never invoked, which is exactly how
	/// a provider with no catalog behaves.</summary>
	public bool SupportsCatalog { get; init; }

	public bool SupportsPush { get; init; }

	public bool SupportsSearch { get; init; }

	/// <summary>Name shown above the catalog tree. Null or empty means "use the integration's name".</summary>
	public string? CatalogName { get; init; }

	/// <summary>
	/// How many bindable entries the catalog holds in total, or null when the provider cannot say
	/// cheaply. Null is not zero: the host shows a count it knows to be partial rather than a wrong
	/// total, because the only other way to learn it is to walk the whole catalog - one request per
	/// container - which is the flooding ADR 0081 exists to prevent.
	/// </summary>
	public int? CatalogEntryCount { get; init; }
}

/// <summary>
/// Tagged union for a variable value - the payload of a <c>set</c> and the body of a
/// <see cref="VariableReadingDto" />. Only text, number and boolean are representable - the three
/// <c>VariableType</c> shapes a provider can declare. Any other CLR value a misbehaving provider
/// returns degrades to <see cref="Kind" /> <c>"unavailable"</c> rather than failing the invocation.
/// </summary>
public sealed record VariableValueDto
{
	/// <summary>One of "text", "number", "boolean", "unavailable".</summary>
	public required string Kind { get; init; }

	public string? Text { get; init; }

	public double? Number { get; init; }

	public bool? Boolean { get; init; }

	public static readonly VariableValueDto Unavailable = new() { Kind = "unavailable" };
}

/// <summary>
/// Mirrors the SDK's <c>VariableReading</c>: one value plus the bounds that apply to it right now. The
/// result of <c>get</c>, and the body of every value that reaches the host through <c>subscribe</c> or a
/// push. The bounds are volatile - a seek position's maximum changes with the track - which is why they
/// travel with the reading rather than with <see cref="VariableDefinitionDto" />.
/// </summary>
public sealed record VariableReadingDto
{
	public required VariableValueDto Value { get; init; }

	public double? Min { get; init; }

	public double? Max { get; init; }

	public double? Step { get; init; }
}

/// <summary>One variable's reading tagged with its provider-local id, mirroring the SDK's
/// <c>VariableValue</c>.</summary>
public sealed record VariableIdValueDto
{
	public required string Id { get; init; }

	public required VariableReadingDto Reading { get; init; }
}

/// <summary>Arguments for the <c>set</c> operation. The variable is addressed by the invoke payload's
/// <c>localId</c>, like <c>get</c>, so only the value travels here.</summary>
public sealed record VariableSetArguments
{
	public required VariableValueDto Value { get; init; }
}

/// <summary>
/// Result of the <c>set</c> operation, mirroring the SDK's <c>VariableWriteResult</c>.
/// </summary>
public sealed record VariableSetResult
{
	/// <summary>One of the SDK's <c>VariableWriteStatus</c> member names. An unrecognised value degrades
	/// to <c>Failed</c> rather than failing the invocation - the same rule
	/// <see cref="VariableValueDto.Kind" /> follows, so a status a later SDK adds never breaks an older
	/// reader.</summary>
	public required string Status { get; init; }

	public LocalizedText? Message { get; init; }
}

/// <summary>Arguments for the <c>discover</c> operation - mirrors the SDK's <c>VariableCatalogQuery</c>.</summary>
public sealed record VariableDiscoverArguments
{
	public string? ParentId { get; init; }

	public string? Search { get; init; }

	public string? ContinuationToken { get; init; }

	public int PageSize { get; init; } = 100;
}

/// <summary>Result of the <c>discover</c> operation - mirrors the SDK's <c>VariableCatalogPage</c>.</summary>
public sealed record VariableCatalogPageResult
{
	public required IReadOnlyList<VariableDefinitionDto> Items { get; init; }

	public string? ContinuationToken { get; init; }
}

/// <summary>Arguments for the <c>resolve</c> operation.</summary>
public sealed record VariableResolveArguments
{
	public required string Id { get; init; }
}

/// <summary>
/// Result of the <c>resolve</c> operation. A wrapper around <see cref="Definition" /> rather than a bare
/// nullable definition, so "this id is not resolvable" is a present result body with an explicit null
/// field rather than an absent body - the invoke plumbing already uses an absent body to mean "no data".
/// </summary>
public sealed record VariableResolveResult
{
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public VariableDefinitionDto? Definition { get; init; }
}

/// <summary>Arguments for the <c>subscribe</c> operation - mirrors <c>IVariableProvider.SubscribeAsync</c>'s
/// <c>localIds</c> parameter.</summary>
public sealed record VariableSubscribeArguments
{
	public required IReadOnlyList<string> Ids { get; init; }
}

/// <summary>Result of the <c>subscribe</c> operation - mirrors <c>IVariableProvider.SubscribeAsync</c>'s
/// return value.</summary>
public sealed record VariableSubscribeResult
{
	public required IReadOnlyList<VariableIdValueDto> Values { get; init; }
}
