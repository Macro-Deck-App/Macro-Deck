using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class CopyImageActionDefinition : ClipboardActionDefinition
{
	public const string FileParameter = "file";

	private static readonly string[] _extensions = ["png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff"];

	public CopyImageActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "copy-image";

	public override LocalizedText Name => Strings.Actions.CopyImage.Name();

	public override LocalizedText Description => Strings.Actions.CopyImage.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Files;

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.File(FileParameter,
			Strings.Actions.CopyImage.FileLabel(),
			Strings.Actions.CopyImage.FileDescription(),
			_extensions,
			true)
	];

	internal static bool IsSupported(string path)
	{
		var extension = Path.GetExtension(path).TrimStart('.');
		return _extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
	}

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		var path = ClipboardActionValues.ReadString(context.Parameters, FileParameter)?.Trim().Trim('"');
		if (string.IsNullOrEmpty(path))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.NoImage());
		}

		if (!Path.IsPathFullyQualified(path))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.PathNotAbsolute(path));
		}

		if (!IsSupported(path))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.UnsupportedImage());
		}

		if (!File.Exists(path))
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.PathNotFound(path));
		}

		await Services.Monitor.WriteAsync(token => Services.Backend.SetImageAsync(path, token),
				ClipboardContentKinds.Image,
				null,
				context.CancellationToken)
			.ConfigureAwait(false);
		return ActionResult.Success();
	}
}
