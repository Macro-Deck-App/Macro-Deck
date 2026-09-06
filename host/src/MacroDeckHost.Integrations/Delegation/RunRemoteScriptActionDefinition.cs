using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Scripts;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Delegation;

internal sealed class RunRemoteScriptActionDefinition : IDynamicOptionsActionDefinition
{
	private const double DefaultTimeoutMilliseconds = 60_000;

	private readonly Func<DelegateRemoteManager?> _manager;

	public RunRemoteScriptActionDefinition(Func<DelegateRemoteManager?> manager) => _manager = manager;

	public string Id => "run-remote-script";

	public LocalizedText Name => AppStrings.Integrations.Delegation.Actions.RunRemoteScriptName();

	public LocalizedText Description => AppStrings.Integrations.Delegation.Actions.RunRemoteScriptDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("instance",
			label: AppStrings.Integrations.Delegation.Actions.InstanceLabel(),
			description: AppStrings.Integrations.Delegation.Actions.InstanceDescription(),
			placeholder: AppStrings.Integrations.Delegation.Actions.InstancePlaceholder()),
		ActionParameter.DynamicChoice("scriptId",
			label: AppStrings.Integrations.Delegation.Actions.ScriptLabel(),
			description: AppStrings.Integrations.Delegation.Actions.ScriptDescription(),
			required: true),
		ActionParameter.Duration("timeout",
			label: AppStrings.Integrations.Delegation.Actions.TimeoutLabel(),
			description: AppStrings.Integrations.Delegation.Actions.TimeoutDescription(),
			defaultMilliseconds: DefaultTimeoutMilliseconds)
	];

	public IActionExecutor CreateExecutor() => new Executor(_manager);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var manager = _manager();
		var result = context.ParameterName switch
		{
			"instance" => InstanceOptionsFor(manager),
			"scriptId" => ScriptOptionsFor(manager, context.CurrentParameters.GetValueOrDefault("instance") as string),
			_ => new DynamicOptionsResult { Options = [] }
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = result.Options,
			AllowsCustomValue = true,
			CacheSeconds = 5,
			Error = result.Error
		});
	}

	private static DynamicOptionsResult InstanceOptionsFor(DelegateRemoteManager? manager)
	{
		var options = manager?.InstanceOptions() ?? [];
		return options.Count == 0
			? new DynamicOptionsResult
				{ Options = [], Error = AppStrings.Integrations.Delegation.Errors.NoneConfigured() }
			: new DynamicOptionsResult { Options = options };
	}

	private static DynamicOptionsResult ScriptOptionsFor(DelegateRemoteManager? manager, string? instanceId)
	{
		var resolution = manager?.Resolve(instanceId) ?? DelegateResolution.NotConfigured;

		return resolution.Kind switch
		{
			DelegateResolutionKind.Found => new DynamicOptionsResult { Options = resolution.Remote!.ScriptOptions() },
			DelegateResolutionKind.NotConfigured => new DynamicOptionsResult
			{
				Options = [], Error = AppStrings.Integrations.Delegation.Errors.NoneConfigured()
			},
			DelegateResolutionKind.Ambiguous => new DynamicOptionsResult
			{
				Options = [], Error = AppStrings.Integrations.Delegation.Errors.Ambiguous()
			},
			DelegateResolutionKind.NotFound => new DynamicOptionsResult
			{
				Options = [], Error = AppStrings.Integrations.Delegation.Errors.InstanceNotFound()
			},
			_ => new DynamicOptionsResult { Options = [] }
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<DelegateRemoteManager?> _manager;

		public Executor(Func<DelegateRemoteManager?> manager) => _manager = manager;

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var manager = _manager();
			if (manager is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Delegation.Errors.NotConfigured());
			}

			var instanceId = context.Parameters.TryGetValue("instance", out var instanceValue)
				? instanceValue.ToString()
				: null;
			var resolution = manager.Resolve(instanceId);

			switch (resolution.Kind)
			{
				case DelegateResolutionKind.NotConfigured:
					return ActionResult.Failed(ActionErrorCodes.NotConfigured,
						AppStrings.Integrations.Delegation.Errors.NoneConfigured());
				case DelegateResolutionKind.Ambiguous:
					return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
						AppStrings.Integrations.Delegation.Errors.Ambiguous());
				case DelegateResolutionKind.NotFound:
					return ActionResult.Failed(ActionErrorCodes.NotFound,
						AppStrings.Integrations.Delegation.Errors.InstanceNotFound());
				case DelegateResolutionKind.Found:
				default:
					break;
			}

			var scriptId = context.Parameters.TryGetValue("scriptId", out var scriptValue)
				? scriptValue.ToString()
				: null;
			if (string.IsNullOrEmpty(scriptId))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Delegation.Errors.NoScriptSelected());
			}

			var timeout = TimeSpan.FromMilliseconds(ReadTimeoutMilliseconds(context));

			return await resolution.Remote!.RunScriptAsync(scriptId,
				context.OriginClientId,
				context.CallDepth + 1,
				ScriptInputParameters.Collect(context.Parameters),
				timeout,
				context.CancellationToken);
		}

		private static double ReadTimeoutMilliseconds(ActionExecutionContext context)
			=> context.Parameters.TryGetValue("timeout", out var value) && value is not null
				? Convert.ToDouble(value, CultureInfo.InvariantCulture)
				: DefaultTimeoutMilliseconds;
	}
}
