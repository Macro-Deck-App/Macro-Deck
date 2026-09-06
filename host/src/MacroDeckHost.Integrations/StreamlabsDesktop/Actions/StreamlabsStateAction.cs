using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

/// <summary>
/// A <see cref="StreamlabsAction" /> whose target is part of the connection's own state, so a button
/// can follow it without asking Streamlabs Desktop anything the integration is not already told.
/// </summary>
internal sealed class StreamlabsStateAction : StreamlabsAction, IStateProviderActionDefinition
{
	private readonly IReadOnlyList<ActionStateDefinition> _states;
	private readonly Func<StreamlabsDesktopState, string> _readActiveStateId;

	public StreamlabsStateAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<StreamlabsDesktopConnection?> resolver,
		Func<StreamlabsDesktopConnection, Task<StreamlabsCommandResult>> command,
		IReadOnlyList<ActionStateDefinition> states,
		Func<StreamlabsDesktopState, string> readActiveStateId)
		: base(id, name, description, resolver, command)
	{
		_states = states;
		_readActiveStateId = readActiveStateId;
	}

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = Connection()?.State;
		var activeId = state is { IsConnected: true } ? _readActiveStateId(state) : ActionStates.Unavailable.Id;
		return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(_states, activeId));
	}
}
