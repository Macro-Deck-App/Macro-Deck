using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class DiscordAction : IActionDefinition
{
	private readonly Func<DiscordConnection?> _resolver;
	private readonly Func<DiscordConnection, CancellationToken, Task> _command;

	public DiscordAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<DiscordConnection?> resolver,
		Func<DiscordConnection, CancellationToken, Task> command)
	{
		Id = id;
		Name = name;
		Description = description;
		_resolver = resolver;
		_command = command;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger = IntegrationLog.For<DiscordAction>(DiscordIntegration.IntegrationId);

		private readonly Func<DiscordConnection?> _resolver;
		private readonly Func<DiscordConnection, CancellationToken, Task> _command;

		public Executor(
			Func<DiscordConnection?> resolver,
			Func<DiscordConnection, CancellationToken, Task> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			await _command(connection, context.CancellationToken).ConfigureAwait(false);
			return ActionResult.Success();
		}
	}
}
