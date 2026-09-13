using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal sealed class CompanionAction : IDynamicOptionsActionDefinition
{
	private readonly CompanionTargetResolver _resolver;
	private readonly Func<IReadOnlyDictionary<string, object>, CompanionCommand?> _command;

	public CompanionAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		CompanionTargetResolver resolver,
		Func<IReadOnlyDictionary<string, object>, CompanionCommand?> command)
	{
		Id = id;
		Name = name;
		Description = description;
		Parameters = [CompanionTargetResolver.Parameter(), .. parameters];
		_resolver = resolver;
		_command = command;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(_resolver.ConfigurationOptions());

	private sealed class Executor : IActionExecutor
	{
		private readonly CompanionTargetResolver _resolver;
		private readonly Func<IReadOnlyDictionary<string, object>, CompanionCommand?> _command;

		public Executor(CompanionTargetResolver resolver,
			Func<IReadOnlyDictionary<string, object>, CompanionCommand?> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var deviceId, out var gateway, out var error))
			{
				return error;
			}

			if (_command(context.Parameters) is not { } command)
			{
				return CompanionTargetResolver.InvalidParameter();
			}

			return await gateway.SendAsync(deviceId, command, context.CancellationToken)
				? ActionResult.Success()
				: CompanionTargetResolver.NotConnected();
		}
	}
}
