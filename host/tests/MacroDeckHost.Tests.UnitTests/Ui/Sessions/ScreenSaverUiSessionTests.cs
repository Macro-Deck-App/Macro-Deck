using MacroDeck.Localization;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Application.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class ScreenSaverUiSessionTests
{
	private const string ProviderId = "com.example.home";

	private ManualTimeProvider _time = null!;
	private UiSessionRegistry _registry = null!;
	private InMemoryDeviceRepository _devices = null!;
	private ScreenSaverRegistry _screenSavers = null!;
	private string _screenSaverId = null!;
	private UiSessionBroker _broker = null!;
	private ScreenSaverUiSessionOpener _opener = null!;

	[SetUp]
	public async Task SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new UiSessionRegistry(_time);
		_devices = new InMemoryDeviceRepository();
		_screenSavers = TestScreenSaverProviders.RegistryWithBuiltInClock();
		_screenSaverId = (await _screenSavers.Register(ProviderId,
			new ScreenSaverDescriptor("photos", LocalizedText.FromLiteral("Photos"), Interactive: true))).ScreenSaverId;

		_broker = new UiSessionBroker(new AcceptingProviderResolver(ProviderId, BuiltInScreenSavers.ProviderId),
			new RecordingUiSessionTransport(),
			_registry,
			new PluginSessionRegistry(_time, Serilog.Core.Logger.None),
			new StubIntegrationRegistry(),
			_time,
			Serilog.Core.Logger.None);

		var services = new ServiceCollection().AddSingleton<IDeviceRepository>(_devices).BuildServiceProvider();
		_opener = new ScreenSaverUiSessionOpener(services.GetRequiredService<IServiceScopeFactory>(),
			_screenSavers,
			_registry,
			_broker);
	}

	[TearDown]
	public void TearDown()
	{
		_broker.Dispose();
		_registry.Dispose();
	}

	[Test]
	public async Task AConnectionWithoutADevice_IsRejected()
	{
		var response = await _opener.OpenAsync(null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
		});
	}

	[Test]
	public async Task ADeviceWithNoSelection_GetsTheBuiltInClock()
	{
		var device = await Device(null);

		var response = await _opener.OpenAsync(device.Id, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(response.ScreenSaverId, Is.EqualTo(BuiltInScreenSavers.Clock));
			Assert.That(response.Interactive, Is.False);
		});
	}

	[Test]
	public async Task ASelectedScreenSaver_OpensAgainstItsProvider_WithTheDeviceAndConfigurationOnTheSurface()
	{
		var device = await Device(_screenSaverId, """{"album":"holiday"}""");

		var response = await _opener.OpenAsync(device.Id, CancellationToken.None);
		var session = _registry.Find(response.SessionId)!;

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(response.Interactive, Is.True);
			Assert.That(session.ProviderId, Is.EqualTo(ProviderId));
			Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.ScreenSaver));
			Assert.That(session.Surface.Attributes[UiScreenSaverSurfaceAttributes.DeviceId].GetString(),
				Is.EqualTo(device.Id.ToString()));
			Assert.That(session.Surface.Attributes[UiScreenSaverSurfaceAttributes.ScreenSaverId].GetString(),
				Is.EqualTo(_screenSaverId));
			Assert.That(session.Surface.Attributes[UiScreenSaverSurfaceAttributes.Configuration]
					.GetProperty("album").GetString(),
				Is.EqualTo("holiday"));
		});
	}

	[Test]
	public async Task ASelectionNothingProvides_FallsBackToTheClock_AndLeavesTheDeviceUntouched()
	{
		var device = await Device("com.example.gone::photos", """{"album":"holiday"}""");

		var response = await _opener.OpenAsync(device.Id, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(response.ScreenSaverId, Is.EqualTo(BuiltInScreenSavers.Clock));
			Assert.That(device.ScreenSaverId, Is.EqualTo("com.example.gone::photos"));
			Assert.That(device.ScreenSaverConfiguration, Is.EqualTo("""{"album":"holiday"}"""));
		});
	}

	[Test]
	public async Task OpeningAgainForTheSameDevice_ReusesItsSession()
	{
		var device = await Device(_screenSaverId);

		var first = await _opener.OpenAsync(device.Id, CancellationToken.None);
		var second = await _opener.OpenAsync(device.Id, CancellationToken.None);

		Assert.That(second.SessionId, Is.EqualTo(first.SessionId));
	}

	[Test]
	public async Task AChangedSelection_OpensANewSession_RatherThanReusingTheOldOne()
	{
		var device = await Device(_screenSaverId);
		var first = await _opener.OpenAsync(device.Id, CancellationToken.None);

		device.ScreenSaverId = null;
		var second = await _opener.OpenAsync(device.Id, CancellationToken.None);

		device.ScreenSaverId = _screenSaverId;
		device.ScreenSaverConfiguration = """{"album":"b"}""";
		var third = await _opener.OpenAsync(device.Id, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(second.ScreenSaverId, Is.EqualTo(BuiltInScreenSavers.Clock));
			Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
			Assert.That(third.SessionId, Is.Not.EqualTo(first.SessionId));
		});
	}

	private async Task<DeviceEntity> Device(string? screenSaverId, string? configuration = null)
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = "hash",
			Name = "Kitchen tablet",
			ClientType = DeviceClientType.WebClient,
			ScreenSaverEnabled = true,
			ScreenSaverId = screenSaverId,
			ScreenSaverConfiguration = configuration,
			CreatedAt = DateTime.UtcNow
		};
		await _devices.Create(device);
		return device;
	}

	private sealed class AcceptingProviderResolver : IUiSessionProviderResolver
	{
		private readonly HashSet<string> _providerIds;

		public AcceptingProviderResolver(params string[] providerIds) => _providerIds = [.. providerIds];

		public IUiSessionProvider? Resolve(string providerId)
			=> _providerIds.Contains(providerId) ? new AcceptingProvider(providerId) : null;
	}

	private sealed class AcceptingProvider : IUiSessionProvider
	{
		public AcceptingProvider(string providerId) => ProviderId = providerId;

		public string ProviderId { get; }

		public Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command, CancellationToken cancellationToken)
			=> Task.FromResult(UiSessionOpenOutcome.Accept(command.UiModelVersion));

		public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public Task DispatchEventAsync(
			string sessionId,
			UiSessionEventCommand command,
			CancellationToken cancellationToken)
			=> Task.CompletedTask;
	}
}
