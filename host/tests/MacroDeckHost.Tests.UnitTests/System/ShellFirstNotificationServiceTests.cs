using MacroDeckHost.Integrations.System.Notifications;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ShellFirstNotificationServiceTests
{
	[Test]
	public async Task A_notification_the_shell_showed_is_not_shown_a_second_time()
	{
		var platform = new RecordingNotificationService();
		var service = new ShellFirstNotificationService(new StubBridge { Result = true }, platform);

		await service.ShowAsync("Title", "Message");

		Assert.That(platform.Shown, Is.Empty);
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task A_notification_the_shell_cannot_show_still_reaches_the_user(bool bridgeThrows)
	{
		var platform = new RecordingNotificationService();
		var bridge = bridgeThrows
			? new StubBridge { Failure = new InvalidOperationException("bridge is broken") }
			: new StubBridge { Result = false };
		var service = new ShellFirstNotificationService(bridge, platform);

		await service.ShowAsync("A plugin wants to pair", "Example Plugin");

		Assert.That(platform.Shown, Is.EqualTo(new[] { ("A plugin wants to pair", "Example Plugin") }));
	}

	[Test]
	public void An_attached_shell_makes_notifications_possible_where_the_platform_alone_cannot()
	{
		var platform = new RecordingNotificationService { IsSupported = false };

		Assert.That(new ShellFirstNotificationService(new StubBridge(), platform).IsSupported, Is.False);
		Assert.That(new ShellFirstNotificationService(new StubBridge { IsAttached = true }, platform).IsSupported,
			Is.True);
	}

	private sealed class StubBridge : IShellNotificationBridge
	{
		public bool IsAttached { get; init; }

		public bool Result { get; init; }

		public Exception? Failure { get; init; }

		public Task<bool> TryDispatchAsync(
			string title,
			string message,
			CancellationToken cancellationToken = default)
			=> Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);

		public Task<IReadOnlyList<ShellNotification>> WaitAsync(
			TimeSpan hold,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public void Report(long id, bool shown) => throw new NotSupportedException();
	}

	private sealed class RecordingNotificationService : INotificationService
	{
		public List<(string Title, string Message)> Shown { get; } = [];

		public bool IsSupported { get; init; } = true;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
		{
			Shown.Add((title, message));
			return Task.CompletedTask;
		}
	}
}
