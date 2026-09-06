using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal class MeldCommandAction : IActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;
	private readonly Func<MeldConnection, CancellationToken, Task<ActionResult>> _command;

	public MeldCommandAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<MeldConnection?> resolver,
		Func<MeldConnection, CancellationToken, Task<ActionResult>> command)
	{
		Id = id;
		Name = name;
		Description = description;
		_resolver = resolver;
		_command = command;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	/// <summary>The connection this action runs against, or null when Meld is not set up.</summary>
	protected MeldConnection? Connection() => _resolver();

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<MeldConnection?> _resolver;
		private readonly Func<MeldConnection, CancellationToken, Task<ActionResult>> _command;

		public Executor(Func<MeldConnection?> resolver,
			Func<MeldConnection, CancellationToken, Task<ActionResult>> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (!MeldTargetResolver.TryRequireConnection(connection, out var error))
			{
				return error!;
			}

			return await _command(connection!, context.CancellationToken).ConfigureAwait(false);
		}
	}
}
