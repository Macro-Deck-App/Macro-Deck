using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using ParameterStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Parameters;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.RunFile;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class RunFileActionDefinition : IActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public RunFileActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.RunFileId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Scripts;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		AutoHotkeyParameters.FilePicker(),
		AutoHotkeyParameters.ArgumentList(),
		ActionParameter.Folder(AutoHotkeyParameters.WorkingDirectory,
			ParameterStrings.WorkingDirectoryLabel(),
			ParameterStrings.WorkingDirectoryDescription()),
		ActionParameter.Toggle(AutoHotkeyParameters.IncludeHelper,
			ParameterStrings.IncludeHelperLabel(),
			ParameterStrings.IncludeHelperDescription(),
			literalOnly: true),
		.. AutoHotkeyParameters.RunOptions()
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_host);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly AutoHotkeyActionHost _host;

		public Executor(AutoHotkeyActionHost host)
		{
			_host = host;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var parameters = context.Parameters;
			if (AutoHotkeyActionValues.ReadText(parameters, AutoHotkeyParameters.File) is not { } file)
			{
				return Task.FromResult(AutoHotkeyActions.MissingTarget(new AutoHotkeyRunRequest
					{ Kind = AutoHotkeyTargetKind.File }));
			}

			var request = new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.File,
				FilePath = file.Trim().Trim('"'),
				Arguments = AutoHotkeyActionValues.ReadStrings(parameters, AutoHotkeyParameters.Arguments),
				WorkingDirectory = AutoHotkeyActionValues.ReadText(parameters, AutoHotkeyParameters.WorkingDirectory),
				IncludeHelper = AutoHotkeyActionValues.ReadBool(parameters, AutoHotkeyParameters.IncludeHelper, false)
			};
			return AutoHotkeyActions.RunAsync(_host, AutoHotkeyActions.WithRunOptions(request, context), context);
		}
	}
}
