using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Errors;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.OpenWindowSpy;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class OpenWindowSpyActionDefinition : IActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public OpenWindowSpyActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.OpenWindowSpyId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Programs;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

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
			var installation = _host.Installation;
			if (!installation.IsValid)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.NotInstalled()));
			}

			if (AutoHotkeyWindowSpy.Find(installation, File.Exists) is not { } script)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotFound, Errors.WindowSpyNotFound()));
			}

			return Task.FromResult(AutoHotkeyWindowSpy.Open(installation.ExecutablePath!, script)
				? ActionResult.Success()
				: ActionResult.Failed(AutoHotkeyRunErrorCodes.ProcessStartFailed, Errors.WindowSpyFailed()));
		}
	}
}
