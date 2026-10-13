using MacroDeck.Sdk.Notifications;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IDesktopNotifier" /> that records what it shows. Supported by default, as Macro Deck reports
/// for a plugin that declares <c>host:desktop-notifications</c> on a computer that offers desktop notifications; set
/// <see cref="IsSupported" /> to <c>false</c> to test the other case, in which nothing is recorded.
/// </summary>
public sealed class FakeDesktopNotifier : IDesktopNotifier
{
	private readonly Lock _gate = new();
	private readonly List<FakeDesktopNotification> _shown = [];

	/// <inheritdoc />
	public bool IsSupported { get; set; } = true;

	/// <summary>Every notification shown so far, oldest first.</summary>
	public IReadOnlyList<FakeDesktopNotification> Shown
	{
		get
		{
			lock (_gate)
			{
				return [.. _shown];
			}
		}
	}

	/// <inheritdoc />
	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(message);
		cancellationToken.ThrowIfCancellationRequested();

		if (IsSupported)
		{
			lock (_gate)
			{
				_shown.Add(new FakeDesktopNotification(title, message));
			}
		}

		return Task.CompletedTask;
	}
}

/// <summary>One notification a <see cref="FakeDesktopNotifier" /> showed.</summary>
public sealed record FakeDesktopNotification(string Title, string Message);
