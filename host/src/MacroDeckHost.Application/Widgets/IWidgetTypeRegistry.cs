using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Widgets;

/// <summary>One entry in the catalog the widget picker renders.</summary>
/// <param name="WidgetTypeId">The id a widget stores as its type: a built-in id, or the qualified
/// <c>owner::local</c> form of a provider-registered one.</param>
/// <param name="ProviderId">The integration or plugin that owns the type. Empty for a built-in, which no
/// integration provides.</param>
/// <param name="Descriptor">What the provider registered, or <see cref="BuiltInWidgetTypes" /> for a
/// built-in.</param>
public sealed record WidgetTypeCatalogEntry(
	string WidgetTypeId,
	string ProviderId,
	WidgetTypeDescriptor Descriptor)
{
	/// <summary>Whether Macro Deck itself ships this type. Structural rather than declared: only a type
	/// nothing provides externally has no owner.</summary>
	public bool IsBuiltIn => ProviderId.Length == 0;
}

/// <summary>
/// The widget types this host knows about.
///
/// <para>
/// The set is open and it is a <i>registry</i> rather than an enumeration for one reason: a widget type
/// is not something only Macro Deck can define. The built-ins register themselves at startup exactly the
/// way a provider does, and every question the rest of the host asks about a type - is it known, what is
/// its id really called given how a client spelled it, what is it called in the reader's language, what
/// does a new one start as - is answered here rather than by a fixed list.
/// </para>
///
/// <para>
/// <b>An unregistered id is not an error.</b> A widget whose type no longer has a provider - an
/// integration removed while its widgets stayed on a deck - is stored, exported and re-imported
/// unchanged; it simply has nothing to draw it. Rejecting it would delete a user's widget because they
/// uninstalled something.
/// </para>
/// </summary>
public interface IWidgetTypeRegistry
{
	/// <summary>Every registered type, built-ins first in the order they were introduced, then the
	/// provider-registered ones by qualified id - so the picker's order does not depend on which
	/// integration happened to start first.</summary>
	IReadOnlyList<WidgetTypeCatalogEntry> All { get; }

	bool IsRegistered(string? id);

	/// <summary>
	/// The registered id a caller's spelling means, or <c>null</c> for one nothing is registered under.
	/// </summary>
	string? Resolve(string? spelling);

	/// <summary>Resolves an id against the live registry. Never throws.</summary>
	bool TryResolve(string? widgetTypeId, out WidgetTypeCatalogEntry entry);

	/// <summary>
	/// Registers a widget type on behalf of an owner, or replaces one already registered under the same
	/// owner and local id.
	/// </summary>
	/// <exception cref="ArgumentException">The owner id or the descriptor's id is invalid or empty, the
	/// name is empty, the default data is not a JSON object, the data schema is unreadable, or the type
	/// declares configuration without a schema.</exception>
	Task<WidgetTypeRegistration> Register(
		string ownerId,
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default);

	/// <summary>Withdraws a widget type. Unknown ids are ignored. Widgets of that type keep their stored
	/// type and data.</summary>
	Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default);

	/// <summary>Withdraws every widget type of one owner - what a stopping integration or an uninstalled
	/// plugin leaves behind. The widgets using them are untouched.</summary>
	Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default);
}
