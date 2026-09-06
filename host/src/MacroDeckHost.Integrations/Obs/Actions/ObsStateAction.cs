using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Obs.Actions;

/// <summary>
/// An <see cref="ObsAction" /> whose target is part of the polled OBS status, so a button can follow
/// it. The read is served from <see cref="ObsConnection.State" /> - the status OBS already refreshes
/// once a second - and costs no request of its own.
/// </summary>
internal sealed class ObsStateAction : ObsAction, IStateProviderActionDefinition
{
	private readonly IReadOnlyList<ActionStateDefinition> _states;
	private readonly Func<ObsState, string> _readActiveStateId;

	public ObsStateAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		ObsTargetResolver resolver,
		Func<ObsConnection, Task<bool>> command,
		IReadOnlyList<ActionStateDefinition> states,
		Func<ObsState, string> readActiveStateId)
		: base(id, name, description, resolver, command)
	{
		_states = states;
		_readActiveStateId = readActiveStateId;
	}

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var connection = ConnectionFor(parameters);
		var state = connection?.State;
		var activeId = state is { IsConnected: true } ? _readActiveStateId(state) : ActionStates.Unavailable.Id;
		return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(_states, activeId));
	}
}
