using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.EmailInbox;

public sealed class EmailInboxUiProvider : IBuiltInIntegrationUiProvider
{
	private readonly IEmailInboxCache _cache;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _time;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IWidgetSampleTextResolver _text;
	private readonly ILogger _logger;
	private readonly TimeZoneInfo _timeZone;

	public EmailInboxUiProvider(
		IEmailInboxCache cache,
		IUiResourceStore resources,
		TimeProvider time,
		IServiceScopeFactory scopeFactory,
		IWidgetSampleTextResolver text,
		ILogger logger,
		TimeZoneInfo? timeZone = null)
	{
		_cache = cache;
		_resources = resources;
		_time = time;
		_scopeFactory = scopeFactory;
		_text = text;
		_logger = logger.ForContext<EmailInboxUiProvider>();
		_timeZone = timeZone ?? TimeZoneInfo.Local;
	}

	public string IntegrationId => EmailWidgetTypes.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return WidgetConfigSurfaces.IsFor(surface, EmailWidgetTypes.QualifiedId)
				? new WidgetConfigSession(new UiView(surface,
					EmailInboxWidgetConfigViews.Build(WidgetConfigSurfaces.Data(surface), _cache)))
				: null;
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
			|| !surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetType, out var type)
			|| type.ValueKind != JsonValueKind.String
			|| type.GetString() != EmailWidgetTypes.QualifiedId)
		{
			return null;
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element)
			&& element.ValueKind == JsonValueKind.Object
				? element
				: default;
		var settings = EmailInboxWidgetSettings.Parse(data);
		var icon = EmailInboxWidgetIcons.EnsureRegistered(_resources);
		var radius = WidgetSafeArea.RadiusOf(surface);
		var format = await FormatAsync().ConfigureAwait(false);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var now = _time.GetUtcNow();
			var sample = await EmailInboxWidgetSample.BuildAsync(_text, now).ConfigureAwait(false);
			var sampleSettings = settings with { Inboxes = [] };
			var state = new UiState<EmailInboxWidgetState>(EmailInboxWidget.Compute(sample, now, sampleSettings, format)
				.State);

			return new StaticWidgetUiSession(new UiView(surface,
				EmailInboxWidgetView.Build(state, sampleSettings, icon, radius)));
		}

		return new EmailInboxWidgetSession(surface,
			_cache,
			_time,
			format,
			TryFormatAsync,
			(snapshot, now, current) => EmailInboxWidget.Compute(snapshot, now, settings, current),
			state => EmailInboxWidgetView.Build(state, settings, icon, radius));
	}

	private async Task<EmailInboxFormat> FormatAsync()
	{
		return new EmailInboxFormat(await TimeFormatResolver.ResolveAsync(_scopeFactory).ConfigureAwait(false),
			_timeZone);
	}

	private async Task<EmailInboxFormat?> TryFormatAsync()
	{
		try
		{
			return await FormatAsync().ConfigureAwait(false);
		}
#pragma warning disable CA1031 // A timer refresh has nobody to report to; the widget keeps its last format.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Reading the time format for an email inbox widget failed");
			return null;
		}
	}
}
