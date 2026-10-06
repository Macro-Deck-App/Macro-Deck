using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class CreateRecordChapterActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string ChapterNameParameter = "chapterName";

	private readonly ObsTargetResolver _resolver;

	public CreateRecordChapterActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "create-record-chapter";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.CreateRecordChapter.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.CreateRecordChapter.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.Text(ChapterNameParameter,
			label: AppStrings.Integrations.Obs.Actions.CreateRecordChapter.ChapterNameLabel(),
			description: AppStrings.Integrations.Obs.Actions.CreateRecordChapter.ChapterNameDescription())
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

			var name = context.Parameters.GetValueOrDefault(ChapterNameParameter) as string;
			var chapter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

			return ObsCommandResults.ToActionResult(await connection.CreateRecordChapterAsync(chapter));
		}
	}
}
