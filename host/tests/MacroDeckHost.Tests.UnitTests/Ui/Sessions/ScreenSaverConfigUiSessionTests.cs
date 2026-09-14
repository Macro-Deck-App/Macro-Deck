using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class ScreenSaverConfigUiSessionTests
{
	private const string DeviceA = "device-a";
	private const string ProviderId = "com.example.home";

	private ManualTimeProvider _time = null!;
	private UiSessionRegistry _registry = null!;
	private UiSessionBroker _broker = null!;
	private IScreenSaverRegistry _screenSavers = null!;
	private string _screenSaverId = null!;
	private ConfigUiSessionOpener _opener = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new UiSessionRegistry(_time);
		(_screenSavers, _screenSaverId) = TestScreenSaverProviders.WithScreenSaver(ProviderId, hasConfiguration: true);
		var integrations = new StubIntegrationRegistry();
		var widgetTypes = new WidgetTypeRegistry(new RecordingMediator());
		var availability = new WidgetProviderAvailability(widgetTypes, integrations, new NoPluginConnections());

		_broker = new UiSessionBroker(new AcceptingResolver(ProviderId),
			new RecordingUiSessionTransport(),
			_registry,
			new PluginSessionRegistry(_time, Serilog.Core.Logger.None),
			integrations,
			_time,
			Serilog.Core.Logger.None);

		_opener = new ConfigUiSessionOpener(integrations,
			new ThrowingConfigFlowManager(),
			TestFolderViewProviders.Registry(),
			_screenSavers,
			new FakeFolderCache(),
			widgetTypes,
			_broker,
			availability,
			new UnavailableWidgetSessionRecovery(_registry, availability, integrations));
	}

	[TearDown]
	public void TearDown()
	{
		_broker.Dispose();
		_registry.Dispose();
	}

	[Test]
	public void The_configuration_opens_against_the_registering_provider_with_the_device_and_values_on_the_surface()
	{
		var ticket = _opener.Open(new OpenConfigUiSessionRequest
			{
				EntryPoint = UiConfigEntryPoints.ScreenSaverConfig,
				IntegrationId = ProviderId,
				DeviceId = DeviceA,
				ScreenSaverId = _screenSaverId,
				ScreenSaverConfiguration = """{"album":"holiday"}"""
			},
			DeviceA);

		var session = _registry.Find(ticket.SessionId)!;

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.True, ticket.Message);
			Assert.That(session.ProviderId, Is.EqualTo(ProviderId));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.EntryPoint].GetString(),
				Is.EqualTo(UiConfigEntryPoints.ScreenSaverConfig));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.DeviceId].GetString(), Is.EqualTo(DeviceA));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.ScreenSaverId].GetString(),
				Is.EqualTo(_screenSaverId));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.ScreenSaverConfiguration]
					.GetProperty("album").GetString(),
				Is.EqualTo("holiday"));
		});
	}

	[Test]
	public async Task A_screensaver_with_nothing_to_configure_is_rejected()
	{
		var plain = await _screenSavers.Register(ProviderId,
			new MacroDeck.Sdk.ScreenSavers.ScreenSaverDescriptor("plain",
				MacroDeck.Localization.LocalizedText.FromLiteral("Plain")));

		var ticket = _opener.Open(new OpenConfigUiSessionRequest
			{
				EntryPoint = UiConfigEntryPoints.ScreenSaverConfig, IntegrationId = ProviderId, ScreenSaverId = plain.ScreenSaverId
			},
			DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderRejected));
		});
	}

	private sealed class AcceptingResolver(string providerId) : IUiSessionProviderResolver
	{
		public IUiSessionProvider? Resolve(string candidate)
			=> string.Equals(candidate, providerId, StringComparison.Ordinal) ? new AcceptingProvider(candidate) : null;
	}

	private sealed class AcceptingProvider(string providerId) : IUiSessionProvider
	{
		public string ProviderId => providerId;

		public Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command, CancellationToken cancellationToken)
			=> Task.FromResult(UiSessionOpenOutcome.Accept(command.UiModelVersion));

		public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

		public Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

		public Task DispatchEventAsync(string sessionId, UiSessionEventCommand command, CancellationToken cancellationToken)
			=> Task.CompletedTask;
	}
}
