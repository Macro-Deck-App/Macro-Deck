using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class VoiceToggleActionDefinition : IActionDefinition, IStateProviderActionDefinition
{
	private readonly Func<DiscordConnection?> _resolver;
	private readonly Func<DiscordState, bool> _read;
	private readonly Func<bool, DiscordState, DiscordVoiceSettingsPatch> _buildPatch;
	private readonly IReadOnlyList<ActionStateDefinition> _states;

	public VoiceToggleActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		LocalizedText onLabel,
		LocalizedText offLabel,
		Func<DiscordConnection?> resolver,
		Func<DiscordState, bool> read,
		Func<bool, DiscordState, DiscordVoiceSettingsPatch> buildPatch,
		IReadOnlyList<ActionStateDefinition> states)
	{
		_states = states;
		Id = id;
		Name = name;
		Description = description;
		Parameters = [DiscordActionParameters.StateSelector(onLabel, offLabel)];
		_resolver = resolver;
		_read = read;
		_buildPatch = buildPatch;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(Id, _resolver, _read, _buildPatch);

	// Discord pushes voice settings over RPC, so the connection's state is already current - the read is
	// the same one the executor uses to decide what a "toggle" means.
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = _resolver()?.State;
		var active = state is { IsConnected: true } ? _read(state) : (bool?)null;
		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(_states, active));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<VoiceToggleActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly string _name;
		private readonly Func<DiscordConnection?> _resolver;
		private readonly Func<DiscordState, bool> _read;
		private readonly Func<bool, DiscordState, DiscordVoiceSettingsPatch> _buildPatch;

		public Executor(
			string name,
			Func<DiscordConnection?> resolver,
			Func<DiscordState, bool> read,
			Func<bool, DiscordState, DiscordVoiceSettingsPatch> buildPatch)
		{
			_name = name;
			_resolver = resolver;
			_read = read;
			_buildPatch = buildPatch;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord voice action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			var state = connection.State;
			var target = DiscordActionParameters.ResolveState(
				context.Parameters.GetValueOrDefault(DiscordActionParameters.StateParameter),
				_read(state));

			var result = await connection
				.SetVoiceSettingsAsync(_buildPatch(target, state), context.CancellationToken)
				.ConfigureAwait(false);

			return DiscordVoiceActionResults.ToActionResult(result, _name);
		}
	}
}
