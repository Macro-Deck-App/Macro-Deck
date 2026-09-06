namespace MacroDeck.Sdk.Variables;

/// <summary>
/// Implemented by integrations that expose variables - sensor readings, live status, or a whole runtime
/// resource space such as Home Assistant entities, OBS sources or MQTT topics.
///
/// <para>
/// One provider, two materialization policies. <see cref="Variables"/> is the eager half: a small, fixed
/// set the host registers and polls from initialization, and the only half a provider has to implement.
/// The catalog half - <see cref="DiscoverAsync"/>, <see cref="ResolveAsync"/> and their neighbours - is
/// opt-in behind <see cref="SupportsCatalog"/>, for a set too large to enumerate: the host never walks
/// it, the user browses it, and only the resources the user actually binds are ever read or watched. A
/// provider that leaves <see cref="SupportsCatalog"/> at its default gets no browse tree and no catalog
/// entry anywhere in the UI.
/// </para>
///
/// <para>
/// Every id crossing this interface is a <em>local</em> id - the part after <c>::</c>. The host qualifies
/// it with the owning integration's id before it reaches persisted configuration and strips the qualifier
/// again before calling back in, so a provider never has to know, parse or produce its own integration
/// id. An eager variable's id is <see cref="Identity.LocalIdKind.Declared"/>; a catalog resource's id is
/// <see cref="Identity.LocalIdKind.Resource"/>, which allows any characters except the reserved
/// <c>::</c> separator, whitespace and control characters, up to
/// <see cref="Identity.MacroDeckId.MaxResourceLocalIdLength"/>.
/// </para>
/// </summary>
public interface IVariableProvider
{
	/// <summary>
	/// The eager variables this provider supplies for its current configuration. Every entry must
	/// declare <see cref="VariableMaterialization.Eager"/> and resolve to a valid declared local id; the
	/// host logs and drops one that does not. Bounded by
	/// <see cref="VariableLimits.MaxEagerVariablesPerProvider"/>.
	/// </summary>
	IReadOnlyList<VariableDefinition> Variables { get; }

	/// <summary>
	/// The eager variables this provider declares, for a capability catalog the host can show on an
	/// integration that is registered but not yet initialized. Reading this must be side-effect free: it
	/// must not connect, spawn a process, touch the network or filesystem, allocate a native handle, or
	/// create any other host state. Reading it never causes a variable to be registered or polled - that
	/// stays <see cref="Variables"/>.
	///
	/// <para>
	/// Defaults to <see cref="Variables"/>. Override this only when <see cref="Variables"/> is itself
	/// gated on a runtime condition (a live connection, a configured account), so the catalog still has
	/// something to show before that condition holds - typically a template built with
	/// <see cref="VariableNameTemplate"/>. A template name has no
	/// <see cref="VariableDefinition.ResolvedId"/>, which is legal here and only here.
	/// </para>
	/// </summary>
	IReadOnlyList<VariableDefinition> DeclaredVariables => Variables;

	/// <summary>
	/// Whether the names in <see cref="DeclaredVariables"/> are templates: each contains a
	/// <see cref="VariableNameTemplate"/> placeholder standing in for a segment that only exists once
	/// the provider is configured (an account, an instance). Defaults to <c>false</c>.
	/// </summary>
	bool VariablesDependOnConfiguration => false;

	/// <summary>
	/// Returns the current reading of one variable, addressed by its
	/// <see cref="VariableDefinition.ResolvedId"/> - not by its name. Return
	/// <see cref="VariableReading.Unavailable"/> for "not right now"; three value shapes are legal
	/// (<see cref="string"/>, a number, <see cref="bool"/>) and any other CLR value is treated as
	/// unavailable.
	///
	/// <para>
	/// May be called concurrently for different variables of the same provider: the host reads each
	/// variable on its own cadence and does not serialize the reads, so that a slow one cannot delay the
	/// rest. Reads of one and the same variable never overlap. Answer from state the provider already
	/// holds wherever possible, and guard any shared mutable state a read touches.
	/// </para>
	/// </summary>
	ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a value to one variable. Only ever called for a definition that declared a
	/// <see cref="VariableDefinition.Write"/>: the host refuses a write to any other variable itself,
	/// without reaching the provider, so a provider need not re-check the declaration.
	///
	/// <para>
	/// Returning <see cref="VariableWriteStatus.Applied"/> means the provider applied the value, not that
	/// the host has seen the result. The host does not echo the requested value into the registry - the
	/// authoritative value arrives on the read side - so a provider that clamps or quantizes what it was
	/// given simply reports the adjusted value on its next read.
	/// </para>
	///
	/// <para>
	/// The default refuses with <see cref="VariableWriteStatus.NotWritable"/>, so a read-only provider
	/// implements nothing.
	/// </para>
	/// </summary>
	ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableWriteResult.NotWritable());

	/// <summary>
	/// Whether this provider offers a browsable catalog of on-demand resources beyond
	/// <see cref="Variables"/>. Defaults to <c>false</c>, which is the behaviour-preserving answer: no
	/// browse tree, no catalog entry, and <see cref="DiscoverAsync"/>/<see cref="ResolveAsync"/> are
	/// never called. At most one provider per plugin session may report <c>true</c>.
	/// </summary>
	bool SupportsCatalog => false;

	/// <summary>
	/// Whether this provider delivers catalog values through <see cref="IVariableSink"/> rather than
	/// waiting to be read. Defaults to <c>false</c>, which makes the host poll every bound resource with
	/// <see cref="ReadAsync"/> on its refresh interval - correct, but linear in the number of bindings. A
	/// provider backed by an event stream should report <c>true</c> and publish instead.
	///
	/// <para>
	/// Read once, when the host describes the provider. It is a property of the provider, not of a
	/// connection: a push provider whose upstream is momentarily down still reports <c>true</c> and
	/// reports its resources as unavailable. It says nothing about the eager half, which the host keeps
	/// polling either way.
	/// </para>
	/// </summary>
	bool SupportsPush => false;

	/// <summary>
	/// Whether <see cref="VariableCatalogQuery.Search"/> is honoured. Defaults to <c>false</c>, and the
	/// host then does not offer a search box for this provider at all rather than filtering locally -
	/// filtering one returned page out of a set the provider is paging would silently hide most matches.
	/// </summary>
	bool SupportsSearch => false;

	/// <summary>
	/// Human-readable name shown above the resource tree, e.g. "Home Assistant". Optional: a provider
	/// that leaves this at the default is described by its integration's name instead.
	/// </summary>
	string CatalogName => string.Empty;

	/// <summary>
	/// How many bindable entries <see cref="DiscoverAsync"/> would yield in total, or <c>null</c> when
	/// that cannot be answered cheaply. Used for a count beside the variables an integration already
	/// has; the host subtracts the ones it has bound.
	///
	/// <para>
	/// Must be side-effect free and must not enumerate the catalog to produce the number: a provider
	/// that would have to walk its own resources to count them should leave this <c>null</c>, and the
	/// host then shows what it has loaded so far instead of a total. Read whenever the catalog is
	/// described, so it may change between reads as the underlying resources do.
	/// </para>
	/// </summary>
	int? CatalogEntryCount => null;

	/// <summary>
	/// Returns one page of the on-demand resources the user can bind. The host calls this for the browser
	/// only - never to build a working set - so a provider may return whatever slice is cheap to produce
	/// and leave the rest behind a continuation token. Every entry must declare
	/// <see cref="VariableMaterialization.OnDemand"/> and carry a valid resource
	/// <see cref="VariableDefinition.Id"/>; the host drops one that does not.
	///
	/// <para>
	/// <see cref="VariableCatalogQuery.ParentId"/> walks the hierarchy: <c>null</c> asks for the roots,
	/// and any other value asks for the children of that node. A flat provider ignores it and returns
	/// everything at the root.
	/// </para>
	/// </summary>
	/// <returns>
	/// A page. Return <see cref="VariableCatalogPage.Empty"/> rather than throwing when the provider
	/// cannot enumerate right now - a disconnected integration has no resources to show, which is not a
	/// failure the user needs a dialog about.
	/// </returns>
	ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableCatalogPage.Empty);

	/// <summary>
	/// Resolves a single local id that may never have come out of <see cref="DiscoverAsync"/> - a user
	/// typing a tag name the provider could not have enumerated, or an id read back out of a profile
	/// saved months ago.
	///
	/// <para>
	/// The default answers from <see cref="Variables"/> by
	/// <see cref="VariableDefinition.ResolvedId"/>, so a provider with no catalog needs no override.
	/// </para>
	/// </summary>
	/// <returns>
	/// The definition, or <c>null</c> when this id is not one this provider owns.
	///
	/// <para>
	/// <c>null</c> means <em>invalid</em>, not <em>absent</em>. A resource that is merely gone right now -
	/// an unplugged ADB device, a deleted OBS source, an integration that is not connected - must still
	/// resolve, because the host distinguishes the two: an unresolvable id is reported to the user as a
	/// broken reference, while a resolvable one whose value reads as unavailable simply resumes working
	/// when the resource comes back. When a provider genuinely cannot tell the two apart, resolve the id:
	/// a reference that survives a restart is worth more than an early error.
	/// </para>
	/// </returns>
	ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Variables.FirstOrDefault(v =>
			string.Equals(v.ResolvedId, localId, StringComparison.Ordinal)));

	/// <summary>
	/// Declares the complete set of catalog resources the host currently cares about, replacing whatever
	/// was declared before. The host calls this whenever the bound set changes, so a provider must treat
	/// <paramref name="localIds"/> as the authoritative working set rather than as an increment - which
	/// also means an empty set is a legitimate call meaning "watch nothing".
	///
	/// <para>
	/// A push provider may only publish values for ids in the most recent set; anything else is dropped
	/// by the host. A provider that does not push still receives this call and may use it to narrow what
	/// it keeps in memory.
	/// </para>
	/// </summary>
	/// <returns>
	/// The current reading of each requested id, so binding a resource does not cost a second round trip.
	/// A provider that has nothing cached yet may return an empty list; the host then falls back to
	/// <see cref="ReadAsync"/>.
	/// </returns>
	ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	/// <summary>
	/// Called once, before the first <see cref="SubscribeAsync"/>, handing the provider the sink its
	/// pushes go into. Safe to retain until the integration shuts down. Called only for a provider that
	/// reports both <see cref="SupportsCatalog"/> and <see cref="SupportsPush"/>; the default is a no-op,
	/// so a provider that only answers reads implements nothing.
	/// </summary>
	Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
