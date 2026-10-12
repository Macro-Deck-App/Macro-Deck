namespace MacroDeck.Sdk.Email;

/// <summary>Which messages <see cref="IEmailInboxProvider.GetSnapshotAsync" /> returns.</summary>
public sealed record EmailMessageQuery
{
	/// <summary>The largest <see cref="MaxMessages" /> the host asks for.</summary>
	public const int MaxMessagesLimit = 50;

	private readonly int _maxMessages = 10;

	/// <summary>How many of the newest messages to return, from 1 to <see cref="MaxMessagesLimit" />.
	/// Defaults to 10.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is outside 1 to
	/// <see cref="MaxMessagesLimit" />.</exception>
	public int MaxMessages
	{
		get => _maxMessages;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxMessagesLimit);
			_maxMessages = value;
		}
	}

	/// <summary>Return only unread messages: the newest unread ones wherever they are in the inbox, not
	/// the unread ones among the newest.</summary>
	public bool UnreadOnly { get; init; }
}
