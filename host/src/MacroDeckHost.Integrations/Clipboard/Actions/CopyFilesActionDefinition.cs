using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class CopyFilesActionDefinition : ClipboardActionDefinition
{
	public const string PathsParameter = "paths";

	private const string PathItem = "path";

	public CopyFilesActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "copy-files";

	public override LocalizedText Name => Strings.Actions.CopyFiles.Name();

	public override LocalizedText Description => Strings.Actions.CopyFiles.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Files;

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Array(PathsParameter,
				ActionParameter.File(PathItem, Strings.Actions.CopyFiles.PathLabel(), required: true),
				Strings.Actions.CopyFiles.PathsLabel(),
				Strings.Actions.CopyFiles.PathsDescription())
			.WithTemplatedItems()
	];

	internal static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string> items)
	{
		return
		[
			.. items.Select(item => item.Trim().Trim('"').Trim())
				.Where(item => item.Length > 0)
		];
	}

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		var paths = NormalizePaths(ClipboardActionValues.ReadStrings(context.Parameters, PathsParameter));
		if (paths.Count == 0)
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.NoPaths());
		}

		foreach (var path in paths)
		{
			if (!Path.IsPathFullyQualified(path))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.PathNotAbsolute(path));
			}

			if (!File.Exists(path) && !Directory.Exists(path))
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.PathNotFound(path));
			}
		}

		await Services.Monitor.WriteAsync(token => Services.Backend.SetFilesAsync(paths, token),
				ClipboardContentKinds.Files,
				null,
				context.CancellationToken)
			.ConfigureAwait(false);
		return ActionResult.Success();
	}
}
