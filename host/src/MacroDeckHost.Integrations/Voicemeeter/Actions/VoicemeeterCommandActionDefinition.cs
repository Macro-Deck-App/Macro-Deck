using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed record VoicemeeterCommand(string Value, LocalizedText Label, string Parameter, float Argument = 1f);

internal sealed class VoicemeeterCommandActionDefinition : IActionDefinition
{
	private const string CommandParameter = "command";

	private readonly IReadOnlyList<VoicemeeterCommand> _commands;
	private readonly Func<VoicemeeterConnection?> _resolver;

	public VoicemeeterCommandActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		LocalizedText label,
		IReadOnlyList<VoicemeeterCommand> commands,
		Func<VoicemeeterConnection?> resolver)
	{
		Id = id;
		Name = name;
		Description = description;
		_commands = commands;
		_resolver = resolver;

		Parameters =
		[
			ActionParameter.Choice(CommandParameter,
				options: commands
					.Select(command => new ActionParameterOption { Value = command.Value, Label = command.Label })
					.ToList(),
				label: label,
				defaultValue: commands[0].Value,
				required: true)
		];
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_commands, _resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<VoicemeeterCommandActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly IReadOnlyList<VoicemeeterCommand> _commands;
		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(IReadOnlyList<VoicemeeterCommand> commands, Func<VoicemeeterConnection?> resolver)
		{
			_commands = commands;
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var selected = VoicemeeterActionValues.ReadText(context.Parameters, CommandParameter);
			var command = _commands.FirstOrDefault(candidate =>
				string.Equals(candidate.Value, selected, StringComparison.Ordinal));

			if (connection is null)
			{
				_logger.Warning("Voicemeeter command action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (command is null)
			{
				_logger.Warning("Voicemeeter command action skipped: '{Command}' is not a known command", selected);
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoCommandSelected()));
			}

			connection.SetParameter(command.Parameter, command.Argument);
			return ActionResult.SucceededTask;
		}
	}
}
