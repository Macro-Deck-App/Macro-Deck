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
		=> CreateAsync(declaration.Name ?? string.Empty,
			declaration.Type,
			initialValue,
			declaration.DecimalPlaces,
			declaration.Id);

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
	Task RequestRefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
	Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
	Task<IVariableHistoryWindow> OpenHistoryAsync(string name,
		int capacity,
		CancellationToken cancellationToken = default)
		=> Task.FromException<IVariableHistoryWindow>(new VariableHistoryException(
			VariableHistoryErrorCode.Unsupported,
			"This Macro Deck keeps no variable history."));
}
