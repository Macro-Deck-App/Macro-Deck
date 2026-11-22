using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Clock;

public sealed class ClockWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly VariableRegistry _variables;
	private readonly IVariableChangeNotifier _notifier;

	public ClockWidgetUiProvider(VariableRegistry variables, IVariableChangeNotifier notifier)
	{
		_variables = variables;
		_notifier = notifier;
	}

	public string WidgetTypeId => WidgetTypeIds.Clock;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
		{
			var config = ClockWidgetData.Parse(DataElement(request.Surface));
			var caption = config.ShowLabel
				? WidgetCaptionTemplate.Create(config.Label,
					_variables,
					_notifier,
					VariableScopeWidgetId(request.Surface))
				: null;

			try
			{
				var view = new UiView(request.Surface,
					ClockWidgetView.Build(config, WidgetSafeArea.RadiusOf(request.Surface), caption?.Text));

				return Task.FromResult<IUiSession?>(new ViewWidgetUiSession(view, caption));
			}
			catch
			{
				caption?.Dispose();

				throw;
			}
		}

		if (request.Surface.Kind == UiSurfaceKinds.Config && WidgetConfigSurfaces.IsFor(request.Surface, WidgetTypeId))
		{
			var data = WidgetConfigSurfaces.Data(request.Surface);
			var configView = new UiView(request.Surface, ClockWidgetConfigView.Build(data));

			return Task.FromResult<IUiSession?>(new WidgetConfigSession(configView));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	private static string? VariableScopeWidgetId(UiSurface surface)
	{
		var attribute = surface.Kind == UiSurfaceKinds.Widget
			? UiWidgetSurfaceAttributes.WidgetId
			: UiWidgetSurfaceAttributes.VariableScopeWidgetId;

		return surface.Attributes.TryGetValue(attribute, out var id)
			&& id.ValueKind == JsonValueKind.String
			&& Guid.TryParse(id.GetString(), out var widgetId)
				? widgetId.ToString()
				: null;
	}

	private static JsonElement DataElement(UiSurface surface)
	{
		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
	}
}
