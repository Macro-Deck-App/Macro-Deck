using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MacroDeckHost.Tests.UnitTests.Host;

[TestFixture]
internal sealed class BackgroundServiceFailureTests
{
	[Test]
	public async Task A_background_service_that_throws_does_not_stop_the_host()
	{
		var startupServices = new ServiceCollection();
		new Startup().ConfigureServices(startupServices);

		var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		foreach (var descriptor in startupServices.Where(d => d.ServiceType == typeof(IConfigureOptions<HostOptions>)))
		{
			builder.Services.Add(descriptor);
		}

		builder.Services.AddSingleton<ThrowingBackgroundService>();
		builder.Services.AddHostedService(sp => sp.GetRequiredService<ThrowingBackgroundService>());

		using var host = builder.Build();
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
		var failing = host.Services.GetRequiredService<ThrowingBackgroundService>();

		await host.StartAsync();
		try
		{
			Assert.ThrowsAsync<InvalidOperationException>(() => failing.ExecuteTask!);

			await Task.WhenAny(Task.Delay(Timeout.Infinite, lifetime.ApplicationStopping),
				Task.Delay(TimeSpan.FromSeconds(2)));

			Assert.That(lifetime.ApplicationStopping.IsCancellationRequested, Is.False);
		}
		finally
		{
			await host.StopAsync();
		}
	}

	private sealed class ThrowingBackgroundService : BackgroundService
	{
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await Task.Yield();
			throw new InvalidOperationException("database unavailable");
		}
	}
}
