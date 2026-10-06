using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class OutputActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private readonly ObsTargetResolver _resolver;
	private readonly Func<ObsConnection, string, Task<ObsOutputOutcome>> _command;
	private readonly bool _providesState;

	public OutputActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		ObsTargetResolver resolver,
		Func<ObsConnection, string, Task<ObsOutputOutcome>> command,
		bool providesState = false)
	{
		Id = id;
		Name = name;
		Description = description;
		_resolver = resolver;
		_command = command;
		_providesState = providesState;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ObsOutputSupport.OutputChoice()
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> ObsOutputSupport.OptionsAsync(_resolver, context);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (!_providesState ||
			parameters.GetValueOrDefault(ObsOutputSupport.OutputParameter) is not string { Length: > 0 } output ||
			string.IsNullOrWhiteSpace(output))
		{
			return null;
		}

		var connection = _resolver.ForOptions(parameters);
		var active = connection is null ? null : await connection.GetOutputActiveCachedAsync(output);
		return ActionStates.Snapshot(ActionStates.OnOff, active);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly ObsTargetResolver _resolver;
		private readonly Func<ObsConnection, string, Task<ObsOutputOutcome>> _command;

		public Executor(ObsTargetResolver resolver, Func<ObsConnection, string, Task<ObsOutputOutcome>> command)
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

			if (ObsOutputSupport.SelectedOutput(context.Parameters) is not { } output)
			{
				return ObsOutputSupport.NoOutputSelected();
			}

			return ObsOutputSupport.FromOutcome(await _command(connection, output), output);
		}
	}
}
