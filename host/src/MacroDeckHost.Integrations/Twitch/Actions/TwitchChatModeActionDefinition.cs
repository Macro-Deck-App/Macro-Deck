using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Twitch.Actions;

/// <summary>
/// "Set Chat Mode", which can also drive a button's state: the chat settings this reads are the ones
/// EventSub already keeps current on the account connection, so a button following a mode costs no
/// Helix request and reflects a change made from the Twitch dashboard just as fast.
/// </summary>
internal sealed class TwitchChatModeActionDefinition : TwitchActionDefinition, IStateProviderActionDefinition
{
	internal const string ModeParameterName = "mode";

	public TwitchChatModeActionDefinition(
		Func<TwitchAccountManager> accounts,
		Func<IVariableApi?> variables,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<TwitchActionScope, Task> execute)
		: base(accounts, variables, id, name, description, parameters, execute)
	{
	}

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(ModeParameterName)?.ToString() is not { Length: > 0 } mode)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var settings = ResolveAccount(parameters.GetValueOrDefault(AccountParameterName))?.State.ChatSettings;
		var enabled = settings is null
			? null
			: mode switch
			{
				"emote-only" => settings.EmoteOnly,
				"followers-only" => settings.FollowersOnly,
				"slow" => settings.SlowMode,
				"subscribers-only" => settings.SubscriberOnly,
				"unique-chat" => settings.UniqueChat,
				_ => null
			};

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.OnOff, enabled));
	}
}
