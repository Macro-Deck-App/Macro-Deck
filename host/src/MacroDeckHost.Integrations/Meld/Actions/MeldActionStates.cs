using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

/// <summary>
/// Reads a Meld target's state for a state-provider action. Everything comes from the session Meld
/// already pushes to <see cref="MeldConnection.State" />, so following a button costs nothing extra.
/// </summary>
internal static class MeldActionStates
{
	/// <summary>
	/// Resolves a configured target the same way the executors do - by id first, then by name - and maps
	/// its state onto <paramref name="states" />. An unconfigured target is "nothing to say" (null); a
	/// disconnected Meld or a target that no longer exists is the set's unavailable state.
	/// </summary>
	public static ActionStateSnapshot? Snapshot<T>(
		MeldConnection? connection,
		object? configuredValue,
		IReadOnlyList<ActionStateDefinition> states,
		Func<MeldSession, IReadOnlyDictionary<string, T>> byId,
		Func<MeldSession, IEnumerable<T>> all,
		Func<T, string> nameOf,
		Func<MeldConnection, T, bool> read)
		where T : class
	{
		if (configuredValue?.ToString() is not { Length: > 0 } value)
		{
			return null;
		}

		if (connection is null || !connection.IsConnected)
		{
			return ActionStates.Snapshot(states, null);
		}

		var session = connection.State.Session;
		var target = byId(session).GetValueOrDefault(value) ??
			all(session).FirstOrDefault(item => string.Equals(nameOf(item), value, StringComparison.OrdinalIgnoreCase));

		return ActionStates.Snapshot(states, target is null ? null : read(connection, target));
	}
}
