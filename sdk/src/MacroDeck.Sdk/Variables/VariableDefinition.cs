using MacroDeck.Localization;

namespace MacroDeck.Sdk.Variables;

/// <summary>
/// When the host brings a variable into existence.
/// </summary>
public enum VariableMaterialization
{
	/// <summary>
	/// Registered and polled from the moment the provider is initialized, because the provider's whole
	/// set is small enough to hold. These are the definitions <see cref="IVariableProvider.Variables"/>
	/// and <see cref="IVariableProvider.DeclaredVariables"/> return.
	/// </summary>
	Eager = 0,

	/// <summary>
	/// Brought into existence only once a user binds it, because the provider's set is a runtime
	/// resource space too large to enumerate - Home Assistant entities, OBS sources, MQTT topics. These
	/// are the definitions <see cref="IVariableProvider.DiscoverAsync"/> and
	/// <see cref="IVariableProvider.ResolveAsync"/> return.
	/// </summary>
	OnDemand = 1
}

/// <summary>
/// Declares that a variable's owner accepts <see cref="IVariableProvider.SetValueAsync"/> for it.
/// Its presence, not the outcome of a write, is what lets a client offer an editing control at all - so
/// a provider that can only sometimes write still declares the capability and refuses the individual
/// write with <see cref="VariableWriteStatus.Unavailable"/>.
/// </summary>
public sealed record VariableWriteCapability
{
	/// <summary>
	/// When <c>true</c>, a continuous control (a Slider) sends the value once the user lets go instead
	/// of streaming intermediate values while dragging. Set it when every intermediate value is a
	/// disruptive side effect of its own - seeking a track jumps the audio - and leave it off for
	/// controls like volume, where live feedback during the drag is the point.
	/// </summary>
	public bool CommitOnRelease { get; init; }
}

/// <summary>
/// One variable a <see cref="IVariableProvider"/> exposes - whether it is part of the provider's fixed
/// eager set or one resource out of a browsable catalog. <see cref="Materialization"/> says which, and
/// the host validates that declaration against the surface the definition arrived on.
/// </summary>
public sealed record VariableDefinition
{
	/// <summary>
	/// The provider-local id, stable for as long as this variable means the same thing. This is what
	/// ends up in a user's persisted configuration, so changing it for an existing variable breaks that
	/// configuration exactly the way changing an action id would.
	///
	/// <para>
	/// Required for an <see cref="VariableMaterialization.OnDemand"/> definition, where it must satisfy
	/// <see cref="Identity.LocalIdKind.Resource"/>. Optional for an
	/// <see cref="VariableMaterialization.Eager"/> one: leaving it null makes the host derive one from
	/// <see cref="Name"/> - see <see cref="ResolvedId"/>.
	/// </para>
	/// </summary>
	public string? Id { get; init; }

	/// <summary>
	/// The canonical variable name for an <see cref="VariableMaterialization.Eager"/> definition - what
	/// the user types into a template, <c>system_volume_percent</c>.
	///
	/// <para>
	/// For an <see cref="VariableMaterialization.OnDemand"/> definition it is instead the name to
	/// suggest when the user binds this resource, and a hint only: the host lowercases it, replaces
	/// anything outside <c>[a-z0-9_]</c>, and appends a suffix if the name is already taken, so a
	/// provider need not produce a legal one. Leaving it null there makes the host derive a name from
	/// <see cref="Id"/>, which is correct but usually uglier - <c>office_temperature</c> reads better
	/// than <c>entity_sensor_office_temperature_state</c>.
	/// </para>
	/// </summary>
	public string? Name { get; init; }

	/// <summary>
	/// The shape of the value <see cref="IVariableProvider.ReadAsync"/> returns for this variable.
	/// Ignored when <see cref="IsBindable"/> is <c>false</c>.
	/// </summary>
	public required VariableType Type { get; init; }

	/// <summary>
	/// Whether the host registers this variable up front or only once a user binds it. A declared
	/// policy, not a hint: the host checks it against the surface the definition came from and drops a
	/// definition that contradicts it, so a catalog cannot smuggle a variable into the eager set or the
	/// other way round.
	/// </summary>
	public required VariableMaterialization Materialization { get; init; }

	/// <summary>
	/// What the user sees instead of <see cref="Name"/>, resolved in the reader's language.
	/// </summary>
	/// <remarks>
	/// <see cref="Name"/> stays the canonical identity - the host derives <see cref="ResolvedId"/> from
	/// it when no <see cref="Id"/> is given, and a template name's placeholder is substituted into it -
	/// so it cannot itself become a localized reference without changing what a variable *is* between
	/// languages. A catalog resource whose display text is runtime data rather than authored text
	/// (an OBS source's title, a Home Assistant friendly name) passes it as a literal.
	/// </remarks>
	public LocalizedText DisplayName { get; init; }

	/// <summary>Longer explanatory text, resolved in the reader's language. Optional.</summary>
	public LocalizedText Description { get; init; }

	/// <summary>
	/// An icon id from the host's icon set. Optional; a browser falls back to a generic node icon.
	/// </summary>
	public string? Icon { get; init; }

	/// <summary>Digits shown for a <see cref="VariableType.Numeric"/> variable. Optional.</summary>
	public int? DecimalPlaces { get; init; }

	/// <summary>
	/// The unit the value is expressed in, as the symbol a reader expects to see next to it -
	/// <c>%</c>, <c>GB</c>, <c>dB</c>, <c>packets/s</c>. A plain string rather than a
	/// <see cref="LocalizedText"/> because a unit symbol is notation, not prose. Optional; a variable
	/// that is a bare count has none.
	///
	/// <para>
	/// Static: it is a property of the variable, not of the reading, so it is declared here rather than
	/// returned from <see cref="IVariableProvider.ReadAsync"/>. It reaches a template as
	/// <c>vars.x.unit</c> while <c>vars.x</c> stays the scalar value.
	/// </para>
	/// </summary>
	public string? Unit { get; init; }

	/// <summary>
	/// How a client should render the value beyond its unit - see <see cref="VariableSemanticKinds"/>
	/// for the kinds the host understands. An open string rather than an enum: a kind the host does not
	/// recognise is rendered as a plain number with its <see cref="Unit"/>, never an error, so a
	/// provider may name one that a later host release learns to format.
	/// </summary>
	public string? SemanticKind { get; init; }

	/// <summary>
	/// Extra provider-defined attributes, reachable from a template as <c>vars.x.&lt;key&gt;</c>. The
	/// host stores and forwards them without interpreting them, and a key colliding with a typed
	/// attribute name does not shadow it.
	///
	/// <para>
	/// Bounded at the host boundary by <see cref="VariableLimits.MaxAttributeEntries"/> and
	/// <see cref="VariableLimits.MaxAttributeValueLength"/>, and keys must match <c>[a-z0-9_]</c>. An
	/// entry that violates any of those is dropped; the variable itself still registers.
	/// </para>
	/// </summary>
	public IReadOnlyDictionary<string, string>? Attributes { get; init; }

	/// <summary>
	/// The configured instance this variable reports on, or <c>null</c> when the provider has a single
	/// implicit one. The host groups a variable under its provider and then under this, so a user with
	/// two OBS connections can tell the two <c>current_scene</c> variables apart.
	/// </summary>
	public VariableConfiguration? Configuration { get; init; }

	/// <summary>
	/// How often the host should re-read this variable when the provider does not push. Optional; the
	/// host's default applies when it is null.
	/// </summary>
	public TimeSpan? RefreshInterval { get; init; }

	/// <summary>
	/// The catalog node this one hangs under, or <c>null</c> for a root. Must be the <see cref="Id"/> of
	/// a definition the same provider can also produce, so the host can walk back up from a stored
	/// binding. Only meaningful for an <see cref="VariableMaterialization.OnDemand"/> definition.
	/// </summary>
	public string? ParentId { get; init; }

	/// <summary>
	/// Whether this node has children worth asking for. A browser shows a disclosure control for it and
	/// calls <see cref="IVariableProvider.DiscoverAsync"/> with
	/// <see cref="VariableCatalogQuery.ParentId"/> set to this <see cref="Id"/> when the user opens it.
	/// Declaring it here rather than making the browser probe speculatively is what keeps opening a leaf
	/// free.
	///
	/// <para>
	/// A container may also be bindable in its own right - a Home Assistant entity that has a state as
	/// well as attributes - which is what <see cref="IsBindable"/> is for.
	/// </para>
	/// </summary>
	public bool IsContainer { get; init; }

	/// <summary>
	/// Whether the user may bind this node to a variable. Defaults to <c>true</c>; set it <c>false</c>
	/// for a grouping node that carries no value of its own.
	/// </summary>
	public bool IsBindable { get; init; } = true;

	/// <summary>
	/// Non-null when the owner accepts <see cref="IVariableProvider.SetValueAsync"/> for this variable.
	/// The host refuses a write to a definition that declares none without ever calling the provider, so
	/// this is the single place "this can be set" is stated.
	/// </summary>
	public VariableWriteCapability? Write { get; init; }

	/// <summary>Whether this variable declares a write capability.</summary>
	public bool CanWrite => Write is not null;

	/// <summary>
	/// The id the host addresses this variable by: <see cref="Id"/> when the provider set one, otherwise
	/// the one derived from <see cref="Name"/>. Null when neither yields a valid id - which is legal
	/// only for a <see cref="VariableNameTemplate"/> name in
	/// <see cref="IVariableProvider.DeclaredVariables"/>, where there is nothing to address yet.
	/// </summary>
	public string? ResolvedId => Id ?? VariableDefinitionId.FromName(Name);

	/// <summary>An eager variable, the shape a provider's fixed catalog is built from.</summary>
	public static VariableDefinition Eager(
		string name,
		VariableType type,
		int? decimalPlaces = null,
		TimeSpan? refreshInterval = null)
		=> new()
		{
			Name = name,
			Type = type,
			Materialization = VariableMaterialization.Eager,
			DecimalPlaces = decimalPlaces,
			RefreshInterval = refreshInterval
		};

	/// <summary>An on-demand catalog resource, addressed by its provider-local resource id.</summary>
	public static VariableDefinition OnDemand(string id, VariableType type)
		=> new() { Id = id, Type = type, Materialization = VariableMaterialization.OnDemand };
}

/// <summary>
/// One configured instance of a provider - an OBS connection, a bot server, a signed-in account.
/// </summary>
/// <param name="Key">
/// Identifies the instance among the declaring integration's own instances. Opaque to the host: any value
/// that is unique within the integration and stable for the instance's lifetime will do, so a provider
/// whose instances are not configuration entries can group by whatever identity it does have. A blank key
/// is treated as no configuration at all.
/// </param>
/// <param name="Name">
/// What the user sees for the group, normally the title they gave the instance. Shown beneath the
/// provider's own name, so it should not repeat it.
/// </param>
public sealed record VariableConfiguration(string Key, LocalizedText Name);
