using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SetRecordDirectoryActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string DirectoryParameter = "directory";

	private readonly ObsTargetResolver _resolver;

	public SetRecordDirectoryActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-record-directory";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetRecordDirectory.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetRecordDirectory.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.Text(DirectoryParameter,
			label: AppStrings.Integrations.Obs.Actions.SetRecordDirectory.DirectoryLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetRecordDirectory.DirectoryDescription(),
			required: true)
	];

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

			if (context.Parameters.GetValueOrDefault(DirectoryParameter) is not string directory ||
				string.IsNullOrWhiteSpace(directory))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoDirectorySelected());
			}

			return ObsCommandResults.ToActionResult(await connection.SetRecordDirectoryAsync(directory.Trim()));
		}
	}
}
