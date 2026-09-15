using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class StoreRegistryRefreshBroadcastBackgroundServiceTests
{
	[Test]
	public async Task Admin_sessions_see_a_refresh_run_and_the_registry_status_its_failure_leaves_behind()
	{
		var tracker = new StoreRegistryRefreshTracker(TimeProvider.System);
		var transport = new RecordingUiTransport();
		using var service = new StoreRegistryRefreshBroadcastBackgroundService(tracker, transport);
		await service.StartAsync(CancellationToken.None);

		tracker.Begin(StoreRegistryRefreshTrigger.Manual);
		tracker.Finish(StoreRegistryRefreshRunState.Failed,
			RegistryRefreshError.NetworkFailure,
			"offline",
			new StoreRegistryStatus { HasCatalog = true, LastError = RegistryRefreshError.NetworkFailure, Stale = true });
		await WaitFor(() => transport.GroupMessages.Count >= 3);
		await service.StopAsync(CancellationToken.None);

		var messages = transport.GroupMessages.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(messages.Select(message => message.Group), Is.All.EqualTo(UiAdminGroups.Admin));
			Assert.That(messages, Has.Count.EqualTo(3));
			Assert.That(((StoreRegistryRefreshChangedEvent)messages[0].Message).Run.State,
				Is.EqualTo(StoreRegistryRefreshRunState.Running));
			Assert.That(((StoreRegistryRefreshChangedEvent)messages[1].Message).Run.State,
				Is.EqualTo(StoreRegistryRefreshRunState.Failed));
			var status = ((StoreRegistryStatusChangedEvent)messages[2].Message).Registry;
			Assert.That(status.Stale, Is.True);
			Assert.That(status.LastError, Is.EqualTo(nameof(RegistryRefreshError.NetworkFailure)));
		});
	}

	private static async Task WaitFor(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}
}
