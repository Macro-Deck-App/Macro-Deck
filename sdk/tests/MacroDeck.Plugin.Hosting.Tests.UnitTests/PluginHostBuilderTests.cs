using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class PluginHostBuilderTests
{
	private static PluginHostBuilder Valid() => MacroDeckPlugin.CreatePlugin();

	[Test]
	public void ConfigureServices_callbacks_run_in_registration_order()
	{
		var order = new List<int>();

		using var plugin = Valid()
			.ConfigureServices((_, _) => order.Add(1))
			.ConfigureServices((_, _) => order.Add(2))
			.Build();

		Assert.That(order, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void UseStartup_configures_services_before_middleware_and_uses_one_instance()
	{
		RecordingStartup.Reset();

		using var plugin = Valid().UseStartup<RecordingStartup>().Build();

		Assert.Multiple(() =>
		{
			Assert.That(RecordingStartup.Calls, Is.EqualTo(new[] { "services", "configure" }));
			Assert.That(RecordingStartup.Instances, Is.EqualTo(1));
		});
	}

	[Test]
	public void An_http_client_factory_is_available_without_asking_for_one()
	{
		using var plugin = Valid().Build();

		Assert.That(plugin.Services.GetService<IHttpClientFactory>(), Is.Not.Null);
	}

	[Test]
	public void Options_bind_from_the_MacroDeck_Plugin_section()
	{
		var builder = Valid();
		builder.Configuration["MacroDeck:Plugin:HostUrl"] = "http://127.0.0.1:9999";

		using var plugin = builder.Build();

		Assert.That(plugin.Services.GetRequiredService<IOptions<PluginHostOptions>>().Value.HostUrl,
			Is.EqualTo("http://127.0.0.1:9999"));
	}

	[Test]
	public void The_launch_id_binds_from_the_MacroDeck_Plugin_section()
	{
		var builder = Valid();
		builder.Configuration["MacroDeck:Plugin:LaunchId"] = "launch-123";

		using var plugin = builder.Build();

		Assert.That(plugin.Services.GetRequiredService<IOptions<PluginHostOptions>>().Value.LaunchId,
			Is.EqualTo("launch-123"));
	}

	[Test]
	public void A_registered_integration_is_resolvable_and_constructed_by_dependency_injection()
	{
		using var plugin = Valid()
			.RegisterIntegration<InjectedIntegration>()
			.Build();

		var integration = plugin.Services.GetServices<IPluginIntegration>().OfType<InjectedIntegration>().Single();

		Assert.That(integration.Factory, Is.Not.Null);
	}

	[Test]
	public void A_capability_handler_is_registered_once_however_many_integrations_there_are()
	{
		using var plugin = Valid()
			.RegisterIntegration<InjectedIntegration>()
			.RegisterIntegration<SecondIntegration>()
			.Build();

		var handlers = plugin.Services.GetServices<ICapabilityHandler>().ToList();

		// Counted per handler kind rather than in total: registered per integration, one of these would
		// declare everything it serves once per integration. A new unconditionally registered handler is
		// expected to raise the total and must not silently make this assertion vacuous.
		Assert.Multiple(() =>
		{
			Assert.That(handlers.Select(handler => handler.GetType()),
				Is.Unique,
				"a capability handler is registered per integration rather than once");
			Assert.That(handlers.Select(handler => handler.Kind), Is.Unique);
		});
	}

	[Test]
	public void A_scoped_dependency_captured_by_a_singleton_fails_the_build()
	{
		// ValidateScopes is on in every environment precisely so this is an error at startup rather
		// than corruption under load.
		var exception = Assert.Throws<PluginConfigurationException>(() => Valid()
			.ConfigureServices((_, services) =>
			{
				services.AddScoped<ScopedDependency>();
				services.AddSingleton<CapturingSingleton>();
			})
			.Build());

		Assert.That(exception!.Problems, Has.One.Contains("service graph"));
	}

	[Test]
	public async Task Start_and_stop_run_integrations_through_their_lifecycle()
	{
		var integration = new TestIntegration();

		await using var plugin = Valid()
			.RegisterIntegration(_ => integration)
			.Build();

		await plugin.StartAsync();

		// Initialization is gated on a live connection (#413 step 6) rather than run from StartAsync
		// directly - every IIntegrationContext member is now a call back into the host, so it cannot
		// run before PluginConnectionHostedService has a session. There is no real host in this test,
		// so the connect is simulated the same way PluginSessionConnection itself signals one.
		plugin.Services.GetRequiredService<PluginConnectionState>().RaiseConnected(resumed: false);
		Assert.That(integration.IsInitialized, Is.True);

		await plugin.StopAsync();
		Assert.That(integration.ShutdownCount, Is.EqualTo(1));
	}

	[Test]
	public async Task A_resume_does_not_reinitialize_and_a_reconnect_does()
	{
		var integration = new TestIntegration();

		await using var plugin = Valid()
			.RegisterIntegration(_ => integration)
			.Build();

		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();

		state.RaiseConnected(resumed: false);
		Assert.That(integration.ShutdownCount, Is.EqualTo(0));

		state.RaiseConnected(resumed: true);
		Assert.That(integration.ShutdownCount, Is.EqualTo(0), "a resume must not tear down and rebuild state");

		state.RaiseConnected(resumed: false);
		Assert.That(integration.ShutdownCount, Is.EqualTo(1), "a reconnect that is not a resume must re-initialize");
	}

	[Test]
	public void An_author_background_service_is_registered_before_the_connection_service()
	{
		using var plugin = Valid()
			.ConfigureServices((_, services) => services.AddHostedService<SignallingService>())
			.Build();

		var hosted = plugin.Services.GetServices<IHostedService>().ToList();

		// Hosted services start in registration order, so the position is the ordering guarantee: an
		// author's own service must be up before the socket opens.
		var author = hosted.FindIndex(service => service is SignallingService);
		var connection = hosted.FindIndex(service => service.GetType().Name == "PluginConnectionHostedService");

		Assert.Multiple(() =>
		{
			Assert.That(author, Is.GreaterThanOrEqualTo(0));
			Assert.That(connection, Is.GreaterThan(author));
			Assert.That(hosted[0].GetType().Name, Is.EqualTo("IntegrationLifecycleHostedService"));
		});
	}

	[Test]
	public async Task DisposeAsync_disposes_singleton_services()
	{
		var plugin = Valid()
			.ConfigureServices((_, services) => services.AddSingleton<TrackedDisposable>())
			.Build();

		var disposable = plugin.Services.GetRequiredService<TrackedDisposable>();
		await plugin.DisposeAsync();

		Assert.That(disposable.Disposed, Is.True);
	}

	private sealed class RecordingStartup : IPluginStartup
	{
		public RecordingStartup() => Instances++;

		public static List<string> Calls { get; } = [];

		public static int Instances { get; private set; }

		public static void Reset()
		{
			Calls.Clear();
			Instances = 0;
		}

		public void ConfigureServices(IServiceCollection services) => Calls.Add("services");

		public void Configure(IApplicationBuilder app) => Calls.Add("configure");
	}

	private sealed class InjectedIntegration(IHttpClientFactory factory) : TestIntegration
	{
		public IHttpClientFactory Factory { get; } = factory;
	}

	private sealed class SecondIntegration : TestIntegration;

	private sealed class ScopedDependency;

	private sealed class CapturingSingleton(ScopedDependency dependency)
	{
		public ScopedDependency Dependency { get; } = dependency;
	}

	private sealed class SignallingService : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class TrackedDisposable : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}
}
