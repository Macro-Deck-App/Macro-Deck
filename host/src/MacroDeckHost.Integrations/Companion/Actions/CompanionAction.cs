using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal sealed class CompanionAction : IDynamicOptionsActionDefinition
{
	private readonly CompanionTargetResolver _resolver;
	private readonly Func<IReadOnlyDictionary<string, object>, CompanionDeviceCommand?> _command;
	private readonly string? _capability;

	public CompanionAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		CompanionTargetResolver resolver,
		Func<IReadOnlyDictionary<string, object>, CompanionDeviceCommand?> command,
		string? capability = null)
	{
		Id = id;
		Name = name;
		Description = description;
		Parameters = [CompanionTargetResolver.Parameter(), .. parameters];
		_resolver = resolver;
		_command = command;
		_capability = capability;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_resolver, _command, _capability);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(_resolver.ConfigurationOptions());
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly CompanionTargetResolver _resolver;
		private readonly Func<IReadOnlyDictionary<string, object>, CompanionDeviceCommand?> _command;
		private readonly string? _capability;

		public Executor(
			CompanionTargetResolver resolver,
			Func<IReadOnlyDictionary<string, object>, CompanionDeviceCommand?> command,
			string? capability)
		{
			_resolver = resolver;
			_command = command;
			_capability = capability;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolveTarget(context.Parameters, out var deviceId, out var error))
			{
				return error;
			}

			if (_command(context.Parameters) is not { } command)
			{
				return CompanionTargetResolver.InvalidParameter();
			}

			var (devices, notConnected) = await _resolver.ConnectAsync(deviceId, context.CancellationToken);
			if (devices is null)
			{
				return notConnected!;
			}

			if (_capability is { } capability)
			{
				var (unavailable, prompt, awaitResult) = CompanionCapabilities.Require(devices, deviceId, capability);
				if (unavailable is not null)
				{
					return unavailable;
				}

				if (awaitResult)
				{
					var result = await devices.RequestAsync(deviceId, command, context.CancellationToken);
					return result.Failure is { } failure
						? CompanionCapabilities.Failed(failure, capability, prompt)
						: ActionResult.Success();
				}
			}

			return await devices.SendAsync(deviceId, command, context.CancellationToken)
				? ActionResult.Success()
				: CompanionTargetResolver.NotConnected();
		}
	}
}
