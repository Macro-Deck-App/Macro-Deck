using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
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
			{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind == UiSurfaceKinds.Dialog)
		{
			return Task.FromResult<IUiSession?>(
				TimerWidgetSurfaces.ReadString(request.Surface, UiDialogSurfaceAttributes.ViewId) == DurationDialogViewId
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
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
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

	public TimerWidgetSessionFactory(TimerWidgetCoordinator timers, IHostLockState lockState, IUiTransport transport)
	{
		Timers = timers;
		_lockState = lockState;
		_transport = transport;
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
			Data = data.ValueKind == JsonValueKind.Object ? data.GetRawText() : null,
		})!;
		var settings = TimerWidgetSettings.Parse(data);

		if (surface.Kind == UiSurfaceKinds.Widget &&
			Guid.TryParse(TimerWidgetSurfaces.ReadString(surface, UiWidgetSurfaceAttributes.WidgetId), out var widgetId))
		{
			return new TimerWidgetSession(surface, widgetId, config, settings, Timers, _lockState, _transport);
		}

		var face = new UiState<TimerFace>(TimerFace.Idle(config, Timers.Store.Now));

		return new StaticWidgetUiSession(new UiView(surface,
			TimerWidgetView.Build(face, settings, WidgetSafeArea.RadiusOf(surface))));
	}
}

internal static class TimerWidgetSurfaces
{
	public static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
