using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class DisablingAnIntegrationTests
{
	private IntegrationRegistry _integrations = null!;
	private WidgetTypeRegistry _widgetTypes = null!;
	private LayoutRegistry _layouts = null!;
	private FolderViewRegistry _folderViews = null!;
	private ScreenSaverRegistry _screenSavers = null!;
	private PluginSessionRegistry _pluginSessions = null!;
	private FakePluginConnection _connection = null!;
	private SetIntegrationEnabledRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IVariableService>(new ThrowingVariableService());
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_integrations = new IntegrationRegistry(scopeFactory,
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Serilog.Log.Logger);
		_widgetTypes = new WidgetTypeRegistry(new RecordingMediator(), integrations: _integrations);
		_layouts = new LayoutRegistry(new RecordingMediator(), _integrations);
		_folderViews = new FolderViewRegistry(new RecordingMediator(), _integrations);
		_screenSavers = new ScreenSaverRegistry(new RecordingMediator(), integrations: _integrations);
		_pluginSessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();

		var widgetTypeHost = new WidgetTypeProviderHost(_widgetTypes, TimeProvider.System, Serilog.Core.Logger.None);
		var layoutHost = new LayoutProviderHost(_layouts, TimeProvider.System, Serilog.Core.Logger.None);
		var folderViewHost = new FolderViewProviderHost(_folderViews, TimeProvider.System, Serilog.Core.Logger.None);
		var screenSaverHost = new ScreenSaverProviderHost(_screenSavers, TimeProvider.System, Serilog.Core.Logger.None);
		var videoStreamHost = TestVideoStreams.Host();
		var deviceHost = TestDeviceProviders.Host();

		var initializer = new IntegrationInitializer(scopeFactory,
			new IntegrationLifecycleTests.FakeDeckNavigator(),
			new IntegrationLifecycleTests.FakeScriptApi(),
			new IntegrationLifecycleTests.FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new StubEventBindingTracker(),
			new IntegrationLifecycleTests.FakeNotificationStore(),
			null!,
			null!,
			null!,
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			layoutHost,
			folderViewHost,
			widgetTypeHost,
			screenSaverHost,
			videoStreamHost,
			deviceHost,
			TimeProvider.System,
			Serilog.Log.Logger,
			new MessageBroker(Serilog.Log.Logger),
			TestColors.Watches);

		var lifecycle = new IntegrationLifecycle(_integrations,
			scopeFactory,
			new RecordingMediator(),
			_pluginSessions,
			initializer,
			layoutHost,
			folderViewHost,
			widgetTypeHost,
			screenSaverHost,
			videoStreamHost,
			deviceHost,
			TimeProvider.System,
			Serilog.Log.Logger);

		_handler = new SetIntegrationEnabledRequestMessageHandler(_integrations, lifecycle, new RecordingMediator());
	}

	private Task<SetIntegrationEnabledResponse> SetEnabledAsync(string id, bool enabled)
		=> _handler.Handle(new SetIntegrationEnabledRequest { Id = id, Enabled = enabled }, CancellationToken.None)
			.AsTask();

	private bool Offers(string qualifiedWidgetTypeId)
		=> _widgetTypes.All.Any(entry => entry.WidgetTypeId == qualifiedWidgetTypeId);

	private async Task AttachPluginSessionAsync(string pluginId)
	{
		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = pluginId,
			DisplayName = "Test Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = DateTimeOffset.UtcNow
		};
		await _pluginSessions.Create(record);
		_pluginSessions.TryAttach(record.SessionId, _connection, null);
	}

	[Test]
	public async Task A_built_in_integrations_widget_type_leaves_the_catalog_when_it_is_disabled_and_returns_on_enable()
	{
		await _integrations.RegisterAsync(new ProvidingIntegration("com.example.gauges"));
		await SetEnabledAsync("com.example.gauges", true);
		var offeredWhileEnabled = Offers("com.example.gauges::gauge");

		var disable = await SetEnabledAsync("com.example.gauges", false);
		var offeredWhileDisabled = Offers("com.example.gauges::gauge");

		await SetEnabledAsync("com.example.gauges", true);

		Assert.Multiple(() =>
		{
			Assert.That(disable.Success, Is.True);
			Assert.That(offeredWhileEnabled, Is.True);
			Assert.That(offeredWhileDisabled, Is.False);
			Assert.That(Offers("com.example.gauges::gauge"), Is.True);
		});
	}

	[Test]
	public async Task A_disabled_plugin_loses_its_widget_type_and_a_later_registration_is_not_kept_until_it_is_enabled()
	{
		await _integrations.RegisterAsync(new ProvidingIntegration("com.example.plugin"), IntegrationOrigin.Plugin);
		await AttachPluginSessionAsync("com.example.plugin");
		await _widgetTypes.Register("com.example.plugin", ProvidingIntegration.Gauge);

		await SetEnabledAsync("com.example.plugin", false);
		var goneOnDisable = !Offers("com.example.plugin::gauge");

		var registration = await _widgetTypes.Register("com.example.plugin", ProvidingIntegration.Gauge);
		var keptWhileDisabled = Offers("com.example.plugin::gauge");

		_connection.Sent.Clear();
		await SetEnabledAsync("com.example.plugin", true);
		var askedToReinitialize = _connection.Sent.Any(envelope => envelope.Type == MessageTypes.HostState);
		await _widgetTypes.Register("com.example.plugin", ProvidingIntegration.Gauge);

		Assert.Multiple(() =>
		{
			Assert.That(goneOnDisable, Is.True);
			Assert.That(registration.WidgetTypeId, Is.EqualTo("com.example.plugin::gauge"));
			Assert.That(keptWhileDisabled, Is.False);
			Assert.That(askedToReinitialize, Is.True);
			Assert.That(Offers("com.example.plugin::gauge"), Is.True);
		});
	}

	[Test]
	public async Task A_disabled_plugin_gets_the_same_validation_error_as_an_enabled_one()
	{
		await _integrations.RegisterAsync(new ProvidingIntegration("com.example.plugin"), IntegrationOrigin.Plugin);
		_integrations.SetEnabled("com.example.plugin", false);

		Assert.ThrowsAsync<ArgumentException>(() =>
			_widgetTypes.Register("com.example.plugin", ProvidingIntegration.Gauge with { DefaultData = "[1, 2]" }));
	}

	[Test]
	public async Task A_plugin_that_was_never_configured_still_offers_its_widget_type()
	{
		await _integrations.RegisterAsync(new UnconfiguredIntegration("com.example.unconfigured"), IntegrationOrigin.Plugin);

		await _widgetTypes.Register("com.example.unconfigured", ProvidingIntegration.Gauge);

		Assert.Multiple(() =>
		{
			Assert.That(_integrations.IsEnabled("com.example.unconfigured"), Is.False);
			Assert.That(Offers("com.example.unconfigured::gauge"), Is.True);
		});
	}

	[Test]
	public async Task A_disabled_plugins_layouts_folder_views_and_screen_savers_are_not_kept()
	{
		await _integrations.RegisterAsync(new ProvidingIntegration("com.example.plugin"), IntegrationOrigin.Plugin);
		_integrations.SetEnabled("com.example.plugin", false);

		await _layouts.Register("com.example.plugin",
			new LayoutDescriptor("grid", "Grid", [new LayoutRegion { Id = "main", Kind = LayoutRegionKinds.Grid }]));
		await _folderViews.Register("com.example.plugin", new FolderViewDescriptor("tiles", LocalizedText.FromLiteral("Tiles")));
		await _screenSavers.Register("com.example.plugin",
			new ScreenSaverDescriptor("photos", LocalizedText.FromLiteral("Photos")));

		Assert.Multiple(() =>
		{
			Assert.That(_layouts.TryResolve("com.example.plugin::grid", out _), Is.False);
			Assert.That(_folderViews.GetAll().Any(entry => entry.ProviderId == "com.example.plugin"), Is.False);
			Assert.That(_screenSavers.GetAll().Any(entry => entry.ProviderId == "com.example.plugin"), Is.False);
		});
	}

	[Test]
	public async Task Disabling_a_system_integration_leaves_what_it_provides_in_place()
	{
		await _integrations.RegisterAsync(new ProvidingSystemIntegration());
		await _widgetTypes.Register(ProvidingSystemIntegration.IntegrationId, ProvidingIntegration.Gauge);

		var response = await SetEnabledAsync(ProvidingSystemIntegration.IntegrationId, false);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_integrations.IsEnabled(ProvidingSystemIntegration.IntegrationId), Is.True);
			Assert.That(Offers($"{ProvidingSystemIntegration.IntegrationId}::gauge"), Is.True);
		});
	}

	private class ProvidingIntegration(string id) : IIntegration, IWidgetTypeProvider
	{
		public static readonly WidgetTypeDescriptor Gauge = new("gauge", LocalizedText.FromLiteral("Gauge"));

		public string Id => id;

		public LocalizedText Name => "Test";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized { get; private set; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}

		public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
			=> await context.RegisterWidgetTypeAsync(Gauge, cancellationToken);
	}

	private sealed class UnconfiguredIntegration(string id) : ProvidingIntegration(id), IConfigFlowProvider
	{
		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
	}

	private sealed class ProvidingSystemIntegration() : ProvidingIntegration(IntegrationId), ISystemIntegration
	{
		public const string IntegrationId = "com.example.system";

		public bool IsActive => true;
	}
}
