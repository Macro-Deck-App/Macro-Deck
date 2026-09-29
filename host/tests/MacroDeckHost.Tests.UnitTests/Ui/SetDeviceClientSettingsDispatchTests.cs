using System.Security.Claims;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class SetDeviceClientSettingsDispatchTests
{
	private InMemoryDeviceRepository _devices = null!;
	private RecordingUiTransport _transport = null!;
	private RecordingMediator _mediator = null!;
	private SetDeviceClientSettingsRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new InMemoryDeviceRepository();
		_transport = new RecordingUiTransport();
		_mediator = new RecordingMediator();
		var time = new ManualTimeProvider();
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var service = new DeviceService(_devices,
			new InMemoryRefreshTokenRepository(),
			new DeviceConnectionTracker(new RecordingEventBus(),
				time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			_transport,
			_mediator,
			time,
			new FakeProfileRegistry(),
			new FakeDeviceDeckNavigator(),
			readiness,
			new ProviderDevicePresenceTracker(),
			new FakeIntegrationRegistry(),
			TestScreenSaverProviders.Registry(),
			new DeviceSessionGuard());
		_handler = new SetDeviceClientSettingsRequestMessageHandler(_devices, service);
	}

	[Test]
	public async Task A_device_hides_its_own_settings_button_and_is_told_about_it()
	{
		var device = await SeedDevice(DeviceClientType.WebClient);
		var other = await SeedDevice(DeviceClientType.WebClient);
		using var dispatcher = DispatcherFor(DeviceClaim(device.Id));

		var response = await Dispatch(dispatcher, new { deviceId = other.Id, settingsButtonHidden = true });

		var pushed = _transport.GroupMessages.Where(sent => sent.Message is DeviceClientSettingsChangedEvent).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(device.SettingsButtonHidden, Is.True);
			Assert.That(other.SettingsButtonHidden, Is.False);
			Assert.That(pushed.Select(sent => sent.Group), Is.EqualTo(new[] { UiDeviceGroups.For(device.Id) }));
			Assert.That(((DeviceClientSettingsChangedEvent)pushed.Single().Message).SettingsButtonHidden, Is.True);
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Select(n => n.DeviceId),
				Does.Contain(device.Id));
		});
	}

	[Test]
	public async Task A_device_shows_its_settings_button_again()
	{
		var device = await SeedDevice(DeviceClientType.WebClient, settingsButtonHidden: true);
		using var dispatcher = DispatcherFor(DeviceClaim(device.Id));

		var response = await Dispatch(dispatcher, new { settingsButtonHidden = false });

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(device.SettingsButtonHidden, Is.False);
		});
	}

	[Test]
	public async Task A_request_without_a_setting_changes_nothing()
	{
		var device = await SeedDevice(DeviceClientType.WebClient, settingsButtonHidden: true);
		using var dispatcher = DispatcherFor(DeviceClaim(device.Id));

		var response = await Dispatch(dispatcher, null);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(device.SettingsButtonHidden, Is.True);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task A_connection_without_a_device_claim_is_refused()
	{
		var device = await SeedDevice(DeviceClientType.WebClient);
		using var dispatcher = DispatcherFor();

		var refusal = Assert.ThrowsAsync<UiWebSocketDispatchException>(async () =>
			await Dispatch(dispatcher, new { deviceId = device.Id, settingsButtonHidden = true }));

		Assert.Multiple(() =>
		{
			Assert.That(refusal!.Code, Is.EqualTo("forbidden"));
			Assert.That(device.SettingsButtonHidden, Is.False);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[TestCase(DeviceClientType.Native)]
	[TestCase(DeviceClientType.Provider)]
	[TestCase(DeviceClientType.AdminUi)]
	public async Task A_device_that_is_not_a_web_client_cannot_change_it(DeviceClientType clientType)
	{
		var device = await SeedDevice(clientType);
		using var dispatcher = DispatcherFor(DeviceClaim(device.Id));

		var response = await Dispatch(dispatcher, new { settingsButtonHidden = true });

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(device.SettingsButtonHidden, Is.False);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_that_no_longer_exists_changes_nothing()
	{
		using var dispatcher = DispatcherFor(DeviceClaim(Guid.NewGuid()));

		var response = await Dispatch(dispatcher, new { settingsButtonHidden = true });

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	private async Task<DeviceEntity> SeedDevice(DeviceClientType clientType, bool settingsButtonHidden = false)
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = "hash",
			Name = "Kitchen tablet",
			ClientType = clientType,
			SettingsButtonHidden = settingsButtonHidden
		};
		await _devices.Create(device);
		return device;
	}

	private static Claim DeviceClaim(Guid deviceId) => new(AuthDefaults.DeviceClaim, deviceId.ToString());

	private static async Task<SetDeviceClientSettingsResponse> Dispatch(UiWebSocketDispatcher dispatcher,
		object? argument)
	{
		var response = await dispatcher.DispatchAsync("SetDeviceClientSettings",
			JsonSerializer.SerializeToElement(new[] { argument }, UiWebSocketProtocol.Json),
			CancellationToken.None);
		return response!.Value.Deserialize<SetDeviceClientSettingsResponse>(UiWebSocketProtocol.Json)!;
	}

	private UiWebSocketDispatcher DispatcherFor(params Claim[] claims)
		=> new(connectionId: "connection-1",
			principal: new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
			abort: static () => { },
			labelText: null!,
			subscriptions: null!,
			widgetState: null!,
			widgetStateSubscriptions: null!,
			variableInterest: null!,
			variableBroadcaster: null!,
			getMusicPlayerInstances: null!,
			getMusicPlayerState: null!,
			getWeatherInstances: null!,
			getFolderViews: null!,
			getScreenSavers: null!,
			getDeviceScreenSaver: null!,
			getDeviceClientSettings: null!,
			setDeviceClientSettings: _handler,
			screenSaverUiSessions: null!,
			getWeatherState: null!,
			getVariableCatalogProviders: null!,
			discoverCatalogVariables: null!,
			resolveCatalogVariable: null!,
			reportFolderChanged: null!,
			listUiPreviews: null!,
			logSubscriptions: null!,
			logFileReader: null!,
			deviceConnections: null!,
			musicPlayerClientSync: null!,
			uiSessions: null!,
			configUiSessions: null!,
			widgetUiSessions: null!,
			uiPreviewSessions: null!,
			folderUiSessions: null!,
			modalUiSessions: null!,
			lifetime: null!,
			transport: null!,
			webSocketTransport: null!,
			companions: null!,
			licenses: null!,
			accessTokenCutoff: new AccessTokenCutoff(),
			deviceSessionGuard: new DeviceSessionGuard(),
			videoStreams: null!,
			videoStreamProviders: null!,
			videoStreamConsumer: null!,
			connectionCancellation: CancellationToken.None);
}
