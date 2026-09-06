using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal class StreamlabsAction : IActionDefinition
{
	private readonly Func<StreamlabsDesktopConnection?> _resolver;
	private readonly Func<StreamlabsDesktopConnection, Task<StreamlabsCommandResult>> _command;

	public StreamlabsAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<StreamlabsDesktopConnection?> resolver,
		Func<StreamlabsDesktopConnection, Task<StreamlabsCommandResult>> command)
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

	/// <summary>The connection this action runs against, or null when Streamlabs Desktop is not set up.</summary>
	protected StreamlabsDesktopConnection? Connection() => _resolver();

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<StreamlabsDesktopConnection?> _resolver;
		private readonly Func<StreamlabsDesktopConnection, Task<StreamlabsCommandResult>> _command;

		public Executor(
			Func<StreamlabsDesktopConnection?> resolver,
			Func<StreamlabsDesktopConnection, Task<StreamlabsCommandResult>> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_resolver() is not { } connection)
			{
				return StreamlabsActionResults.NotConnected;
			}

			var result = await _command(connection).ConfigureAwait(false);
			return result.ToActionResult();
		}
	}
}
