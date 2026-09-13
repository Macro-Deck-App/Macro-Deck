using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

public sealed class UnavailableWidgetUiProvider : IUiProvider
{
	private const string ProviderNameAttribute = "unavailableProviderName";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public static Dictionary<string, JsonElement> Naming(
		Dictionary<string, JsonElement> attributes,
		LocalizedText providerName)
	{
		attributes[ProviderNameAttribute] = JsonSerializer.SerializeToElement(providerName);
		return attributes;
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var name = request.Surface.Attributes.TryGetValue(ProviderNameAttribute, out var element)
			? element.Deserialize<LocalizedText>()
			: default;

		UiElement root = request.Surface.Kind == UiSurfaceKinds.Config ? ConfigView(name) : TileView(name);

		return Task.FromResult<IUiSession?>(new UnavailableWidgetSession(new UiView(request.Surface, root)));
	}

	private static UiStack TileView(LocalizedText name)
		=> new UiStack
		{
			Key = "unavailable",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiTextRun
				{
					Key = "message",
					Text = UiText.FromLocalized(() => AppStrings.Deck.UnavailableWidget.Tile(plugin: name)),
					Size = UiSize.FromBasis(0.1),
					Wrap = UiValue.Of(true),
				},
			],
		};

	private static UiWidgetConfiguration ConfigView(LocalizedText name)
		=> new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiTextRun
					{
						Key = "message",
						Text = UiText.FromLocalized(() =>
							AppStrings.Widgets.Editor.ProviderUnavailableMessage(plugin: name)),
						Wrap = UiValue.Of(true),
					},
				],
			},
		};

	private sealed class UnavailableWidgetSession : IUiSession
	{
		private readonly UiView _view;

		public UnavailableWidgetSession(UiView view)
		{
			_view = view;
			_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
			_view.HandlerFaulted += (_, fault)
				=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		}

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree() => _view.Tree;

		public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

		public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
