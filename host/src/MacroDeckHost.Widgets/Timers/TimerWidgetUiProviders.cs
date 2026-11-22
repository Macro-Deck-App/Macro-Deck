using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Timers;

public sealed class CountdownWidgetUiProvider : IBuiltInWidgetUiProvider, IBuiltInIntegrationUiProvider
{
	public const string OwnerId = "app.macro-deck.countdown";

	public const string DurationDialogViewId = "countdown-duration";

	private readonly TimerWidgetSessionFactory _sessions;
	private readonly CountdownDurationDrafts _drafts;

	public CountdownWidgetUiProvider(TimerWidgetSessionFactory sessions, CountdownDurationDrafts drafts)
	{
		_sessions = sessions;
		_drafts = drafts;
	}

	public string WidgetTypeId => WidgetTypeIds.Countdown;

	public string IntegrationId => OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
		new()
			{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind == UiSurfaceKinds.Dialog)
		{
			return Task.FromResult<IUiSession?>(
				TimerWidgetSurfaces.ReadString(request.Surface, UiDialogSurfaceAttributes.ViewId)
				== DurationDialogViewId
					? CountdownDurationDialogSession.Open(request.Surface, _sessions.Timers.Store, _drafts)
					: null);
		}

		return Task.FromResult(_sessions.Create(request.Surface, WidgetTypeId));
	}
}

public sealed class StopwatchWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly TimerWidgetSessionFactory _sessions;

	public StopwatchWidgetUiProvider(TimerWidgetSessionFactory sessions)
	{
		_sessions = sessions;
	}

	public string WidgetTypeId => WidgetTypeIds.Stopwatch;

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

		return Task.FromResult(_sessions.Create(request.Surface, WidgetTypeId));
	}
}

public sealed class TimerWidgetSessionFactory
{
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly VariableRegistry _variables;
	private readonly IVariableChangeNotifier _notifier;
	private readonly VirtualWidgetPress? _virtualPress;

	public TimerWidgetSessionFactory(
		TimerWidgetCoordinator timers,
		IHostLockState lockState,
		IUiTransport transport,
		VariableRegistry variables,
		IVariableChangeNotifier notifier,
		VirtualWidgetPress? virtualPress = null)
	{
		Timers = timers;
		_variables = variables;
		_notifier = notifier;
		_lockState = lockState;
		_transport = transport;
		_virtualPress = virtualPress;
	}

	internal TimerWidgetCoordinator Timers { get; }

	internal IUiSession? Create(UiSurface surface, string widgetTypeId)
	{
		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return WidgetConfigSurfaces.IsFor(surface, widgetTypeId)
				? new WidgetConfigSession(new UiView(surface,
					TimerWidgetConfigView.Build(WidgetConfigSurfaces.Data(surface), widgetTypeId)))
				: null;
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) ? element : default;
		var config = TimerWidgetConfig.FromWidget(new WidgetEntity
		{
			Type = widgetTypeId,
			Data = data.ValueKind == JsonValueKind.Object ? data.GetRawText() : null
		})!;
		var settings = TimerWidgetSettings.Parse(data);
		var caption = settings.ShowLabel
			? WidgetCaptionTemplate.Create(settings.Label, _variables, _notifier, VariableScopeWidgetId(surface))
			: null;

		try
		{
			return Create(surface, config, settings, caption);
		}
		catch
		{
			caption?.Dispose();

			throw;
		}
	}

	private IUiSession Create(
		UiSurface surface,
		TimerWidgetConfig config,
		TimerWidgetSettings settings,
		WidgetCaptionTemplate? caption)
	{
		if (surface.Kind == UiSurfaceKinds.Widget
			&& Guid.TryParse(TimerWidgetSurfaces.ReadString(surface, UiWidgetSurfaceAttributes.WidgetId),
				out var widgetId))
		{
			return new TimerWidgetSession(surface,
				widgetId,
				config,
				settings,
				Timers,
				_lockState,
				_transport,
				caption);
		}

		var face = new UiState<TimerFace>(TimerFace.Idle(config, Timers.Store.Now));

		if (_virtualPress?.VirtualWidgetIdOf(surface) is { } virtualWidgetId)
		{
			return new VirtualWidgetUiSession(surface,
				virtualWidgetId,
				VirtualWidgetGestures.PressAndLongPress,
				_virtualPress,
				_lockState,
				events
					=> TimerWidgetView.Build(face, settings, WidgetSafeArea.RadiusOf(surface), events, caption?.Text),
				caption);
		}

		var view = new UiView(surface,
			TimerWidgetView.Build(face, settings, WidgetSafeArea.RadiusOf(surface), caption: caption?.Text));

		return caption is null ? new StaticWidgetUiSession(view) : new ViewWidgetUiSession(view, caption);
	}

	private static string? VariableScopeWidgetId(UiSurface surface)
	{
		var id = TimerWidgetSurfaces.ReadString(surface,
			surface.Kind == UiSurfaceKinds.Widget
				? UiWidgetSurfaceAttributes.WidgetId
				: UiWidgetSurfaceAttributes.VariableScopeWidgetId);

		return Guid.TryParse(id, out var widgetId) ? widgetId.ToString() : null;
	}
}

internal static class TimerWidgetSurfaces
{
	public static string? ReadString(UiSurface surface, string name)
	{
		return surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}
}
