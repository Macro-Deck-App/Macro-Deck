namespace MacroDeck.Sdk.Variables;

/// <summary>Manages variables owned by the calling integration.</summary>
/// <remarks>The host scopes this API to the integration, preventing access to variables owned elsewhere.</remarks>
public interface IVariableApi
{
	/// <summary>Returns all global variables owned by the integration.</summary>
	Task<IReadOnlyList<VariableHandle>> GetAllAsync();

	/// <summary>Returns an owned variable by canonical name, or <c>null</c> when none exists.</summary>
	Task<VariableHandle?> GetByNameAsync(string name);

	/// <summary>
	/// Creates a variable. Use <paramref name="definitionId"/> for stable catalog variables; otherwise the host derives it from the name.
	/// </summary>
	Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null);

	/// <summary>
	/// Creates a variable from a full declaration, so its display name, attributes and configuration reach
	/// the host as they would for a polled variable. <see cref="VariableDefinition.RefreshInterval"/> is
	/// ignored: a pushed variable has no polling cadence.
	/// </summary>
	/// <remarks>
	/// Defaults to the name-and-type overload, dropping the extra metadata, so a host built before this
	/// existed still satisfies the contract. That fallback needs a <see cref="VariableDefinition.Name"/>
	/// and creates nothing usable without one. An overload rather than optional parameters on the existing
	/// method, which would break every already-compiled caller.
	/// </remarks>
	Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null)
	{
		return CreateAsync(declaration.Name ?? string.Empty,
			declaration.Type,
			initialValue,
			declaration.DecimalPlaces,
			declaration.Id);
	}

	/// <summary>Updates an owned variable's value.</summary>
	Task SetValueAsync(Guid variableId, object? value);

	/// <summary>Deletes an owned variable. Unknown or foreign IDs are ignored.</summary>
	Task DeleteAsync(Guid variableId);

	/// <summary>
	/// Asks Macro Deck to read every declared variable of this integration again now, instead of at each
	/// variable's next refresh interval. Call it after something changed what <c>ReadAsync</c> would answer,
	/// such as a connection that came up. Requests made while one is pending are merged into one read.
	/// </summary>
	/// <remarks>
	/// Best effort: it never throws because the request could not be delivered. A Macro Deck that predates
	/// it, and any implementation that does not override it, does nothing; the variables are still read at
	/// their refresh interval. Pushed variables are unaffected: set those with <see cref="SetValueAsync" />.
	/// </remarks>
	Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Asks Macro Deck to read only the variables with the given definition ids again now, instead of at their
	/// next refresh interval: declared variables, and catalog resources a user bound that Macro Deck polls.
	/// Use it when a change affects a few variables and reading every declared variable would be wasteful.
	/// Ids that match neither are ignored, and an empty collection asks for nothing. Requests made while one is
	/// pending are merged into one read.
	/// </summary>
	/// <remarks>
	/// Best effort, like <see cref="RequestRefreshAsync" />: it never throws because the request could not be
	/// delivered. Reading more than asked for is always allowed, so the default implementation, and a Macro
	/// Deck that predates this member, read every declared variable instead. A pending request for every
	/// declared variable covers any pending request for some. An older Macro Deck ignores catalog resource ids,
	/// so those keep their refresh interval there. Pushed catalog resources are unaffected: publish those
	/// through the sink.
	/// </remarks>
	Task RequestDefinitionRefreshAsync(
		IReadOnlyCollection<string> definitionIds,
		CancellationToken cancellationToken = default)
	{
		return definitionIds.Count == 0 ? Task.CompletedTask : RequestRefreshAsync(cancellationToken);
	}

	/// <summary>
	/// Tells Macro Deck that the set of variables this integration declares has changed, for example
	/// after a configuration entry was added, so it reads the declarations again and starts or stops
	/// reading the affected variables. This does not affect an on-demand variable catalog.
	/// </summary>
	/// <remarks>
	/// Best effort: it never throws because the notice could not be delivered. A plugin's notice travels as a
	/// <c>state.update</c> for the <c>variables</c> capability kind, which every Macro Deck version
	/// understands. An implementation that does not override it does nothing.
	/// </remarks>
	Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Records bindings from resources of this integration's variable catalog to variables, as if the user
	/// had bound them, for example to carry variables over from an older version of the integration. A
	/// resource this integration already has a binding for is left as it is.
	/// </summary>
	/// <param name="requests">The bindings to record. Every name must satisfy
	/// <see cref="VariableNames.IsValid" /> and every resource id must be a valid resource id.</param>
	/// <param name="cancellationToken">Cancels the request before anything is recorded.</param>
	/// <returns>One outcome per request, in request order: <see cref="VariableBindingOutcome.Bound" /> when the
	/// binding was recorded, <see cref="VariableBindingOutcome.AlreadyBound" /> when this integration already
	/// had a binding for that resource, including one earlier in the same call.</returns>
	/// <remarks>
	/// The bindings belong to the calling integration and are recorded together or not at all. Recording does
	/// not create the variables right away: Macro Deck creates them the next time it restores bindings, at the
	/// latest on its next start. A requested name another variable holds by then is replaced with one derived
	/// from it, as for any binding. The default implementation, and a Macro Deck that predates this member, throw
	/// <see cref="NotSupportedException" />; a caller that migrates data should keep its old data until a
	/// call succeeds.
	/// </remarks>
	/// <exception cref="ArgumentException">A name or resource id is invalid; nothing was recorded.</exception>
	/// <exception cref="NotSupportedException">This Macro Deck cannot record bindings.</exception>
	/// <exception cref="InvalidOperationException">The bindings could not be stored; nothing was recorded.</exception>
	Task<IReadOnlyList<VariableBindingOutcome>> BindAsync(
		IReadOnlyCollection<VariableBindingRequest> requests,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<IReadOnlyList<VariableBindingOutcome>>(
			new NotSupportedException("This Macro Deck cannot record variable bindings."));
	}

	/// <summary>
	/// Opens a rolling window of the recent values of one of this integration's own global numeric
	/// variables, keeping at most <paramref name="capacity" /> samples, for example to draw a graph in a
	/// widget. The window may be opened before the variable exists; it fills once the variable reads as an
	/// available number.
	/// </summary>
	/// <param name="name">The canonical name of the variable.</param>
	/// <param name="capacity">How many samples to keep, at least 1. Macro Deck may keep fewer than asked
	/// for; the window documents what it holds through <see cref="IVariableHistoryWindow.Values" />.</param>
	/// <param name="cancellationToken">Cancels opening the window.</param>
	/// <remarks>
	/// The default implementation, and a Macro Deck that predates variable history, throw a
	/// <see cref="VariableHistoryException" /> with <see cref="VariableHistoryErrorCode.Unsupported" />, so a
	/// widget can catch it and draw without a graph.
	/// </remarks>
	/// <exception cref="VariableHistoryException">No window could be opened; see
	/// <see cref="VariableHistoryException.ErrorCode" />.</exception>
	Task<IVariableHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<IVariableHistoryWindow>(new VariableHistoryException(
			VariableHistoryErrorCode.Unsupported,
			"This Macro Deck keeps no variable history."));
	}
}
