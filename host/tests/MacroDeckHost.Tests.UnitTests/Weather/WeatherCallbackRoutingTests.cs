using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherCallbackRoutingTests
{
	private const string PluginId = "com.example.weather";
	private const string SessionId = "session-1";

	private PluginSessionRegistry _sessions = null!;
	private WeatherBroadcastTrigger _trigger = null!;
	private PluginCallbackRouter _router = null!;

	[SetUp]
	public void SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_trigger = new WeatherBroadcastTrigger();
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_router = new PluginCallbackRouter(_sessions,
			null!,
			scopeFactory,
			null!,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			null!,
			new NoOpUiSessionSink(),
			new FakePluginDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			null!,
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new FakeHostLockState(),
			Serilog.Core.Logger.None,
			weatherTrigger: _trigger);
	}

	[TearDown]
	public void TearDown()
	{
		_trigger.Dispose();
	}

	[Test]
	public async Task A_weather_provider_reporting_changed_snapshots_makes_the_host_read_the_stations_now()
	{
		await OpenSessionAsync(true);

		var result = await NotifyAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(await _trigger.WaitAsync(TimeSpan.Zero, CancellationToken.None), Is.True);
		});
	}

	[Test]
	public async Task A_plugin_without_the_weather_capability_is_refused_and_wakes_nothing()
	{
		await OpenSessionAsync(false);

		var result = await NotifyAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(await _trigger.WaitAsync(TimeSpan.Zero, CancellationToken.None), Is.False);
		});
	}

	private async Task OpenSessionAsync(bool declared)
	{
		var capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal);
		if (declared)
		{
			capabilities[CapabilityKinds.Weather] = CapabilityNegotiationResult.Accept(CapabilityKinds.Weather, 1);
		}

		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = SessionId,
			PluginId = PluginId,
			DisplayName = "Weather",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = capabilities,
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
	}

	private Task<HostCallbackResult> NotifyAsync()
	{
		return _router.RouteAsync(PluginId,
			SessionId,
			Guid.CreateVersion7().ToString(),
			new HostInvokePayload { Api = HostApis.Weather, Operation = HostOperations.Weather.SnapshotsChanged },
			CancellationToken.None);
	}
}
