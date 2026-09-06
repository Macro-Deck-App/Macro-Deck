using MacroDeck.Sdk.Notifications;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IUserNotifier" />. Neither method throws, matching the interface's own
/// fire-and-forget contract. A <see cref="UserNotificationRequest.Key" /> makes a notification
/// replaceable - a second <see cref="Notify" /> under the same key updates <see cref="Current" />'s
/// entry instead of adding a second one; a <c>null</c> key never collides with anything, and every such
/// call adds its own independent entry.
/// </summary>
public sealed class FakeUserNotifier : IUserNotifier
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, UserNotificationRequest> _keyed = new(StringComparer.Ordinal);
	private readonly List<UserNotificationRequest> _keyless = [];

	/// <summary>Everything currently active - a replaced or dismissed entry does not appear here.</summary>
	public IReadOnlyList<UserNotificationRequest> Current
	{
		get
		{
			lock (_gate)
			{
				return [.. _keyed.Values, .. _keyless];
			}
		}
	}

	/// <inheritdoc />
	public void Notify(UserNotificationRequest notification)
	{
		if (notification is null)
		{
			return;
		}

		lock (_gate)
		{
			if (notification.Key is { } key)
			{
				_keyed[key] = notification;
			}
			else
			{
				_keyless.Add(notification);
			}
		}
	}

	/// <inheritdoc />
	public void Dismiss(string key)
	{
		if (key is null)
		{
			return;
		}

		lock (_gate)
		{
			_keyed.Remove(key);
		}
	}
}
