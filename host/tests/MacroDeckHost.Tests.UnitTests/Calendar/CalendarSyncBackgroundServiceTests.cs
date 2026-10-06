using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
internal sealed class CalendarSyncBackgroundServiceTests
{
	[Test]
	public async Task A_provider_that_goes_away_leaves_the_calendars_without_waiting_for_the_next_poll()
	{
		var time = new FakeTimeProvider { Now = Noon };
		var integrations = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Logger());
		var provider = new FakeCalendarIntegration("dev.plugin.calendar", "Plugin Calendar")
			.WithAccount("work", "main")
			.WithEvent("work", Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30)));
		await integrations.RegisterAsync(provider, IntegrationOrigin.Plugin);

		var calendars = new CalendarRegistry(integrations, Logger());
		using var cache = new CalendarEventCache(calendars, time, Logger(), TimeZoneInfo.Utc);
		using var signal = new CalendarSyncSignal();
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		using var service = new CalendarSyncBackgroundService(new StartedHostLifetime(),
			cache,
			calendars,
			integrations,
			signal,
			readiness,
			time,
			Logger());
		await service.StartAsync(CancellationToken.None);

		await WaitUntil(() => cache.Snapshot.Events.Count == 1);
		await integrations.UnregisterAsync(provider.Id);
		await WaitUntil(() => cache.Snapshot.Events.Count == 0);

		await service.StopAsync(CancellationToken.None);
		Assert.That(cache.Snapshot.Accounts, Is.Empty);
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met in time.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(canceled: true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
