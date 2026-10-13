namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// The <see cref="IDesktopNotifier" /> of a context that cannot show desktop notifications:
/// <see cref="IsSupported" /> is <c>false</c> and <see cref="ShowAsync" /> shows nothing.
/// </summary>
public sealed class UnsupportedDesktopNotifier : IDesktopNotifier
{
	public static readonly UnsupportedDesktopNotifier Instance = new();

	private UnsupportedDesktopNotifier()
	{
	}

	public bool IsSupported => false;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(message);
		cancellationToken.ThrowIfCancellationRequested();
		return Task.CompletedTask;
	}
}
