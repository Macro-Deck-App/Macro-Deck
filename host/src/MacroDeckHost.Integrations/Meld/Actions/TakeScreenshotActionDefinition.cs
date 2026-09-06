using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class TakeScreenshotActionDefinition : IActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public TakeScreenshotActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "take-screenshot";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.TakeScreenshot.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.TakeScreenshot.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(MeldActionParameters.Orientation,
			options: MeldActionParameters.ScreenshotOrientations,
			label: AppStrings.Integrations.Meld.Parameters.Orientation(),
			defaultValue: MeldActionParameters.OrientationHorizontal)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<MeldConnection?> _resolver;

		public Executor(Func<MeldConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (!MeldTargetResolver.TryRequireConnection(connection, out var error))
			{
				return error!;
			}

			var orientation = context.Parameters.GetValueOrDefault(MeldActionParameters.Orientation) as string ??
				MeldActionParameters.OrientationHorizontal;
			var command = orientation == MeldActionParameters.OrientationVertical
				? MeldObjects.CommandScreenshotVertical
				: MeldObjects.CommandScreenshot;

			return await connection!.SendCommandAsync(command, context.CancellationToken).ConfigureAwait(false);
		}
	}
}
