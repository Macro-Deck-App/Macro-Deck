using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using Serilog;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Errors;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class AutoHotkeyActionHost
{
	public AutoHotkeyActionHost(ILogger logger)
	{
		Logger = logger;
	}

	public ILogger Logger { get; }

	public AutoHotkeyProcessManager? Manager { get; set; }

	public AutoHotkeyScriptStore? Store { get; set; }

	public IUserVariableApi? UserVariables { get; set; }

	public AutoHotkeyInstallation Installation { get; set; } = AutoHotkeyInstallation.NotFound;
}

internal static class AutoHotkeyActions
{
	public const string RunScriptId = "run-script";
	public const string RunFileId = "run-file";
	public const string RunInlineCodeId = "run-inline-code";
	public const string StopId = "stop-script";
	public const string RestartId = "restart-script";
	public const string ToggleId = "toggle-script";
	public const string OpenWindowSpyId = "open-window-spy";

	public static IReadOnlyList<IActionDefinition> Create(AutoHotkeyActionHost host)
	{
		return
		[
			new RunScriptActionDefinition(host),
			new RunFileActionDefinition(host),
			new RunInlineCodeActionDefinition(host),
			new StopScriptActionDefinition(host),
			new RestartScriptActionDefinition(host),
			new ToggleScriptActionDefinition(host),
			new OpenWindowSpyActionDefinition(host)
		];
	}

	public static Task<DynamicOptionsResult> ScriptOptionsAsync(
		AutoHotkeyActionHost host,
		DynamicOptionsContext context)
	{
		if (context.ParameterName != AutoHotkeyParameters.Script)
		{
			return Task.FromResult(new DynamicOptionsResult { Options = [] });
		}

		var store = host.Store;
		if (store is null || store.IsReadOnly)
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [],
				Error = store is null ? Errors.NotInstalled() : Errors.LibraryUnreadable()
			});
		}

		return Task.FromResult(new DynamicOptionsResult
		{
			Options =
			[
				.. store.Scripts.OrderBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase)
					.Select(script => new ActionParameterOption
					{
						Value = script.Id.ToString("D"), Label = script.Name
					})
			]
		});
	}

	public static AutoHotkeyRunRequest WithRunOptions(AutoHotkeyRunRequest request, ActionExecutionContext context)
	{
		var parameters = context.Parameters;
		return request with
		{
			Parameters = AutoHotkeyActionValues.ReadKeyValues(parameters, AutoHotkeyParameters.Parameters),
			Policy = AutoHotkeyActionValues.ReadPolicy(parameters),
			Wait = AutoHotkeyActionValues.ReadBool(parameters, AutoHotkeyParameters.Wait, false),
			Timeout = AutoHotkeyActionValues.ReadDuration(parameters,
				AutoHotkeyParameters.Timeout,
				AutoHotkeyRunRequest.DefaultTimeout),
			VariableContext = UserVariableContext.From(context)
		};
	}

	public static AutoHotkeyRunRequest? TargetRequest<T>(IReadOnlyDictionary<string, T> parameters)
	{
		var target = AutoHotkeyActionValues.ReadText(parameters, AutoHotkeyParameters.Target);
		if (string.Equals(target, AutoHotkeyParameters.FileTarget, StringComparison.Ordinal))
		{
			return AutoHotkeyActionValues.ReadText(parameters, AutoHotkeyParameters.File) is { } file
				? new AutoHotkeyRunRequest { Kind = AutoHotkeyTargetKind.File, FilePath = file.Trim().Trim('"') }
				: null;
		}

		return AutoHotkeyActionValues.ReadGuid(parameters, AutoHotkeyParameters.Script) is { } scriptId
			? new AutoHotkeyRunRequest { Kind = AutoHotkeyTargetKind.Managed, ScriptId = scriptId }
			: null;
	}

	public static string? IdentityOf(AutoHotkeyRunRequest request)
	{
		if (request.Kind == AutoHotkeyTargetKind.Managed && request.ScriptId is { } scriptId)
		{
			return AutoHotkeyIdentity.Managed(scriptId);
		}

		if (request.Kind != AutoHotkeyTargetKind.File || string.IsNullOrEmpty(request.FilePath))
		{
			return null;
		}

		try
		{
			return AutoHotkeyIdentity.File(request.FilePath);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}
	}

	public static ActionResult MissingTarget(AutoHotkeyRunRequest? request)
	{
		return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
			request?.Kind == AutoHotkeyTargetKind.File ? Errors.NoFileSelected() : Errors.NoScriptSelected());
	}

	public static async Task<ActionResult> RunAsync(
		AutoHotkeyActionHost host,
		AutoHotkeyRunRequest request,
		ActionExecutionContext context,
		string? expectedStateId = null)
	{
		if (host.Manager is not { } manager)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.NotInstalled());
		}

		var result = await manager.RunAsync(request, context.CancellationToken).ConfigureAwait(false);
		if (request.Wait
			&& AutoHotkeyActionValues.ReadText(context.Parameters, AutoHotkeyParameters.OutputVariable) is { } variable
			&& (result.Status == AutoHotkeyRunStatus.Completed || result.Output is not null))
		{
			await ActionVariableTarget.WriteAsync(host.UserVariables,
					null,
					variable.Trim(),
					UserVariableContext.From(context),
					VariableType.Text,
					result.Result ?? result.Output?.TrimEnd('\r', '\n') ?? string.Empty,
					host.Logger,
					context.CancellationToken)
				.ConfigureAwait(false);
		}

		return result.Status == AutoHotkeyRunStatus.Failed
			? ActionResult.Failed(result.ErrorCode!, result.ErrorMessage)
			: expectedStateId is null
				? ActionResult.Success()
				: ActionResult.Success(expectedStateId);
	}
}
