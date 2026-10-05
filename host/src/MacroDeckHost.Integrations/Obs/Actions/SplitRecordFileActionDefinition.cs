using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SplitRecordFileActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly ObsTargetResolver _resolver;

	public SplitRecordFileActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "split-record-file";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SplitRecordFile.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SplitRecordFile.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [ObsTargetResolver.Parameter()];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(_resolver.ConfigurationOptions());

	private sealed class Executor : IActionExecutor
	{
		private readonly ObsTargetResolver _resolver;

		public Executor(ObsTargetResolver resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			return ObsCommandResults.ToActionResult(await connection.SplitRecordFileAsync());
		}
	}
}
