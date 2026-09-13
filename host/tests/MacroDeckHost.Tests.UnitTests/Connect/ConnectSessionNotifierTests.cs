using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;
using MacroDeckHost.Connect;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

public class ConnectSessionNotifierTests
{
	private const string Picture = "https://auth.macro-deck.app/assets/v1/org-1/users/user-1/avatar";

	[Test]
	public async Task A_changed_avatar_tells_clients_to_reload_the_session()
	{
		var (notifier, _, avatar, transport) = Create();
		await notifier.StartAsync(CancellationToken.None);

		avatar.RaiseVersionChanged();
		await notifier.StopAsync(CancellationToken.None);
		notifier.Dispose();

		Assert.That(transport.Sent.OfType<ConnectSessionChangedNotification>().Count(), Is.EqualTo(1));
	}

	[Test]
	public async Task Signing_in_revalidates_the_avatar_and_signing_out_does_not()
	{
		var (notifier, session, avatar, _) = Create();
		await notifier.StartAsync(CancellationToken.None);

		session.Publish(FakeConnectSessionService.SignedIn(Picture));
		await ConnectTestHarness.WaitUntil(() => avatar.Revalidations == 1, "signing in did not revalidate the avatar");

		session.Publish(ConnectSessionSnapshot.SignedOut);
		await Task.Delay(100);
		await notifier.StopAsync(CancellationToken.None);
		notifier.Dispose();

		Assert.That(avatar.Revalidations, Is.EqualTo(1));
	}

	[Test]
	public async Task A_failing_revalidation_does_not_stop_notifications()
	{
		var (notifier, session, avatar, transport) = Create();
		avatar.Failure = new InvalidOperationException("disk full");
		await notifier.StartAsync(CancellationToken.None);

		session.Publish(FakeConnectSessionService.SignedIn(Picture));
		await ConnectTestHarness.WaitUntil(() => avatar.Revalidations == 1, "signing in did not revalidate the avatar");
		avatar.RaiseVersionChanged();
		await notifier.StopAsync(CancellationToken.None);
		notifier.Dispose();

		Assert.That(transport.Sent.OfType<ConnectSessionChangedNotification>().Count(), Is.EqualTo(2));
	}

	private static (ConnectSessionNotifier Notifier, FakeConnectSessionService Session, FakeAvatarCache Avatar,
		Auth.RecordingUiTransport Transport) Create()
	{
		var session = new FakeConnectSessionService();
		var avatar = new FakeAvatarCache();
		var transport = new Auth.RecordingUiTransport();

		return (new ConnectSessionNotifier(session, avatar, transport, TimeProvider.System, Log.Logger),
			session,
			avatar,
			transport);
	}

	private sealed class FakeAvatarCache : IConnectAvatarCache
	{
		private int _revalidations;

		public event EventHandler? VersionChanged;

		public string? Version => null;

		public Exception? Failure { get; set; }

		public int Revalidations => Volatile.Read(ref _revalidations);

		public Task<ConnectAvatar?> GetAvatar(CancellationToken cancellationToken = default)
			=> Task.FromResult<ConnectAvatar?>(null);

		public Task Revalidate(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _revalidations);
			return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
		}

		public void RaiseVersionChanged() => VersionChanged?.Invoke(this, EventArgs.Empty);
	}
}
