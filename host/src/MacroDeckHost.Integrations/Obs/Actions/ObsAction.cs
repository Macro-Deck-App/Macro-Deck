using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal class ObsAction : IDynamicOptionsActionDefinition
{
	private readonly ObsTargetResolver _resolver;
	private readonly Func<ObsConnection, Task<bool>> _command;

	public ObsAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		ObsTargetResolver resolver,
		Func<ObsConnection, Task<bool>> command)
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

	public IReadOnlyList<ActionParameter> Parameters { get; } = [ObsTargetResolver.Parameter()];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	/// <summary>The connection this instance's configuration names, or null while it is incomplete.</summary>
	protected ObsConnection? ConnectionFor(IReadOnlyDictionary<string, object?> parameters)
		=> _resolver.ForOptions(parameters);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(_resolver.ConfigurationOptions());

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger = IntegrationLog.For<ObsAction>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;
		private readonly Func<ObsConnection, Task<bool>> _command;

		public Executor(ObsTargetResolver resolver, Func<ObsConnection, Task<bool>> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			return await _command(connection)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}
	}
}
