using MacroDeck.Sdk.Email;

namespace MacroDeckHost.Application.Email;

public abstract record EmailInboxChange(EmailInboxDescriptor Inbox);

public sealed record EmailReceived(EmailInboxDescriptor Inbox, EmailMessage Message) : EmailInboxChange(Inbox);

public sealed record EmailUnreadCountChanged(EmailInboxDescriptor Inbox, int UnreadCount, int PreviousUnreadCount)
	: EmailInboxChange(Inbox);

public sealed record EmailReadStateChanged(EmailInboxDescriptor Inbox, EmailMessage Message) : EmailInboxChange(Inbox);

public sealed record EmailInboxConnected(EmailInboxDescriptor Inbox) : EmailInboxChange(Inbox);

public sealed record EmailInboxDisconnected(EmailInboxDescriptor Inbox) : EmailInboxChange(Inbox);

public sealed record EmailDetection(
	IReadOnlyList<EmailInboxChange> Changes,
	bool? Connected,
	EmailInboxState? Baseline);

public static class EmailChangeDetector
{
	public static EmailDetection Detect(EmailInboxState? baseline, EmailInboxState next, bool? previousConnected)
	{
		var changes = new List<EmailInboxChange>();
		var connected = Definitive(next.Status) ?? previousConnected;

		if (previousConnected is { } was && connected is { } now && was != now)
		{
			changes.Add(now ? new EmailInboxConnected(next.Inbox) : new EmailInboxDisconnected(next.Inbox));
		}

		if (!IsBaseline(next))
		{
			return new EmailDetection(changes, connected, IsBaseline(baseline) ? baseline : null);
		}

		if (IsBaseline(baseline))
		{
			changes.AddRange(Messages(baseline!, next));
		}

		return new EmailDetection(changes, connected, next);
	}

	// Providers answer Connecting or Disconnected with last known data or none, so only a live
	// Connected read is trusted to say which mail already existed.
	private static bool IsBaseline(EmailInboxState? state)
	{
		return state is { Status: EmailInboxStatus.Connected, IsStale: false, HasData: true };
	}

	private static IEnumerable<EmailInboxChange> Messages(EmailInboxState previous, EmailInboxState next)
	{
		var before = previous.Latest.ToDictionary(message => message.Id, StringComparer.Ordinal);
		var overlap = next.Latest.Any(message => before.ContainsKey(message.Id));
		var oldest = previous.Latest.Count == 0
			? (DateTimeOffset?)null
			: previous.Latest.Min(message => message.ReceivedAt);
		var newest = previous.Latest.Count == 0
			? (DateTimeOffset?)null
			: previous.Latest.Max(message => message.ReceivedAt);

		foreach (var message in next.Latest.OrderBy(message => message.ReceivedAt))
		{
			if (before.ContainsKey(message.Id))
			{
				continue;
			}

			if (oldest is null || message.ReceivedAt >= oldest && (overlap || message.ReceivedAt > newest))
			{
				yield return new EmailReceived(next.Inbox, message);
			}
		}

		foreach (var message in next.Latest)
		{
			if (before.TryGetValue(message.Id, out var earlier) && earlier.IsRead != message.IsRead)
			{
				yield return new EmailReadStateChanged(next.Inbox, message);
			}
		}

		if (previous.UnreadCount != next.UnreadCount)
		{
			yield return new EmailUnreadCountChanged(next.Inbox, next.UnreadCount, previous.UnreadCount);
		}
	}

	private static bool? Definitive(EmailInboxStatus status)
	{
		return status switch
		{
			EmailInboxStatus.Connected => true,
			EmailInboxStatus.Disconnected or EmailInboxStatus.Error => false,
			_ => null
		};
	}
}
