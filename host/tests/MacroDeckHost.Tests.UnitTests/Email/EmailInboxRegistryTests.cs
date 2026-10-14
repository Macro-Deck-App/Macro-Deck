using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxRegistryTests
{
	[Test]
	public void Inboxes_are_identified_by_integration_and_local_id_and_invalid_ids_are_skipped()
	{
		var imap = new FakeEmailIntegration("app.imap", "IMAP").WithInbox("work", "  Work  ");
		imap.Inboxes.Add(new EmailInbox { Id = "work", DisplayName = "Duplicate" });
		imap.Inboxes.Add(new EmailInbox { Id = "has space", DisplayName = "Broken" });
		imap.Inboxes.Add(new EmailInbox { Id = "x::y", DisplayName = "Smuggled" });
		using var world = new EmailWorld(imap);

		var inboxes = world.Registry.GetInboxes();

		Assert.That(inboxes.Select(inbox => (inbox.Ref, inbox.LocalId, inbox.DisplayName)),
			Is.EqualTo(new[] { ("app.imap::work", "work", "Work") }));
	}

	[Test]
	public void A_disabled_provider_contributes_no_inboxes()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		using var world = new EmailWorld(new ConfigurableIntegrationRegistry([imap], ["app.imap"]));

		Assert.That(world.Registry.GetInboxes(), Is.Empty);
	}

	[Test]
	public void A_change_raised_by_a_provider_registered_before_the_service_started_is_observed()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		using var world = new EmailWorld(imap);

		world.Registry.ReconcileSubscriptions();
		imap.RaiseChanged("work");
		imap.RaiseChanged(null);

		var pending = world.Signal.TakePending();
		Assert.Multiple(() =>
		{
			Assert.That(pending.Inboxes, Is.EquivalentTo(new[] { ("app.imap", "work") }));
			Assert.That(pending.Integrations, Is.EquivalentTo(new[] { "app.imap" }));
		});
	}

	[Test]
	public async Task A_re_registered_provider_is_followed_by_its_new_instance_only()
	{
		var integrations = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			EmailTesting.Logger());
		var first = new FakeEmailIntegration("app.imap").WithInbox("work");
		await integrations.RegisterAsync(first);
		using var world = new EmailWorld(integrations);
		world.Registry.ReconcileSubscriptions();

		await integrations.UnregisterAsync(first.Id);
		var second = new FakeEmailIntegration("app.imap").WithInbox("work");
		await integrations.RegisterAsync(second);
		world.Registry.ReconcileSubscriptions();
		world.Signal.TakePending();

		first.RaiseChanged("work");
		var fromOld = world.Signal.TakePending();
		second.RaiseChanged("work");
		var fromNew = world.Signal.TakePending();

		Assert.Multiple(() =>
		{
			Assert.That(fromOld.IsEmpty, Is.True);
			Assert.That(fromNew.Inboxes, Is.EquivalentTo(new[] { ("app.imap", "work") }));
		});
	}

	[Test]
	public async Task The_background_service_reads_every_inbox_at_start_and_follows_later_changes()
	{
		var integrations = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			EmailTesting.Logger());
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		await integrations.RegisterAsync(imap);
		using var signal = new EmailSyncSignal();
		using var registry = new EmailInboxRegistry(integrations, signal, EmailTesting.Logger());
		using var cache = new EmailInboxCache(registry,
			new InMemoryEmailInboxKeyStore(),
			signal,
			TimeProvider.System,
			EmailTesting.Logger());
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		using var service = new EmailSyncBackgroundService(new StartedHostLifetime(),
			cache,
			registry,
			integrations,
			signal,
			readiness,
			TimeProvider.System,
			EmailTesting.Logger());

		await service.StartAsync(CancellationToken.None);
		await WaitUntil(() => cache.Snapshot.Inboxes.Count == 1);

		imap.Set("work", EmailTesting.Snapshot(EmailInboxStatus.Connected, 3, []));
		imap.RaiseChanged("work");
		await WaitUntil(() => cache.Snapshot.Inboxes.Single().UnreadCount == 3);

		await integrations.UnregisterAsync(imap.Id);
		await WaitUntil(() => cache.Snapshot.Inboxes.Count == 0);

		await service.StopAsync(CancellationToken.None);
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
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
		public CancellationToken ApplicationStarted { get; } = new(true);

		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;

		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
