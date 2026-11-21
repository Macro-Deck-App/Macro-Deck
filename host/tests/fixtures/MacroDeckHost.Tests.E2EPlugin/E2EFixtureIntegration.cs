using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Tests.E2EPlugin;

public sealed class E2EFixtureIntegration : IPluginIntegration, IVariableProvider, IWidgetTypeProvider, IUiProvider
{
	public const string SetTextActionId = "set-text";

	public const string TextParameterName = "text";

	public const string TextVariableName = "e2e_fixture_text";

	public const string TileWidgetTypeId = "fixture-tile";

	public const string TileText = "E2E fixture tile";

	private const string TextVariableId = "text";

	private volatile string _text = "";

	private string? _tileType;

	public E2EFixtureIntegration()
	{
		Actions = [new SetTextAction(this)];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager(TextVariableName, VariableType.Text) with { Id = TextVariableId }
	];

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared }
	];

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		var registration = await context.RegisterWidgetTypeAsync(TileDescriptor(), cancellationToken);
		_tileType = registration.WidgetTypeId;
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return [TileDescriptor()];
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(string.Equals(localId, TextVariableId, StringComparison.Ordinal)
			? VariableReading.Of(_text)
			: VariableReading.Unavailable);
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
			|| surface.Attributes[UiWidgetSurfaceAttributes.WidgetType].GetString() != _tileType)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var root = new UiTextRun { Key = "tile", Text = TileText, Size = 0.3 };
		return Task.FromResult<IUiSession?>(new ViewSession(new UiView(surface, root)));
	}

	private static WidgetTypeDescriptor TileDescriptor()
	{
		return new WidgetTypeDescriptor(TileWidgetTypeId, "E2E fixture tile", "A tile that shows a fixed text.");
	}

	private sealed class SetTextAction(E2EFixtureIntegration integration) : IActionDefinition
	{
		public string Id => SetTextActionId;

		public LocalizedText Name => "Set text";

		public LocalizedText Description => "Stores the text so the e2e_fixture_text variable reports it.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
			[ActionParameter.Text(TextParameterName, "Text", required: true)];

		public IActionExecutor CreateExecutor()
		{
			return new Executor(integration);
		}
	}

	private sealed class Executor(E2EFixtureIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (context.Parameters.GetValueOrDefault(TextParameterName) is not string text)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter, "text is required."));
			}

			integration._text = text;
			return ActionResult.SucceededTask;
		}
	}

	private sealed class ViewSession : IUiSession
	{
		private readonly UiView _view;

		public ViewSession(UiView view)
		{
			_view = view;
			_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
			_view.HandlerFaulted += (_, fault)
				=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		}

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree()
		{
			return _view.Tree;
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return _view.DrainPatches();
		}

		public void Dispatch(UiEvent uiEvent)
		{
			_view.Dispatch(uiEvent);
		}

		public ValueTask DisposeAsync()
		{
			_view.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
