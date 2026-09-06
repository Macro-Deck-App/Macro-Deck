using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

/// <summary>
/// A <see cref="MeldCommandAction" /> whose target Meld reports in its own session state, so a button
/// running it can follow the real streaming or recording state instead of what it last did.
/// </summary>
internal sealed class MeldStateCommandAction : MeldCommandAction, IStateProviderActionDefinition
{
	private readonly IReadOnlyList<ActionStateDefinition> _states;
	private readonly Func<MeldState, bool> _readActive;

	public MeldStateCommandAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<MeldConnection?> resolver,
		Func<MeldConnection, CancellationToken, Task<ActionResult>> command,
		IReadOnlyList<ActionStateDefinition> states,
		Func<MeldState, bool> readActive)
		: base(id, name, description, resolver, command)
	{
		_states = states;
		_readActive = readActive;
	}

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var connection = Connection();
		var active = connection is { IsConnected: true } ? _readActive(connection.State) : (bool?)null;
		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(_states, active));
	}
}
