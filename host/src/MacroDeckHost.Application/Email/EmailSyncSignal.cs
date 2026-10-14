namespace MacroDeckHost.Application.Email;

public sealed record EmailSyncRequest(
	bool All,
	IReadOnlySet<string> Integrations,
	IReadOnlySet<(string IntegrationId, string InboxId)> Inboxes)
{
	public static readonly EmailSyncRequest Everything
		= new(true, new HashSet<string>(StringComparer.Ordinal), new HashSet<(string, string)>());

	public static readonly EmailSyncRequest Nothing
		= new(false, new HashSet<string>(StringComparer.Ordinal), new HashSet<(string, string)>());

	public bool IsEmpty => !All && Integrations.Count == 0 && Inboxes.Count == 0;

	public bool Covers(EmailInboxDescriptor inbox)
	{
		return All
			|| Integrations.Contains(inbox.IntegrationId)
			|| Inboxes.Contains((inbox.IntegrationId, inbox.LocalId));
	}

	public EmailSyncRequest Union(EmailSyncRequest other)
	{
		return new EmailSyncRequest(All || other.All,
			new HashSet<string>(Integrations.Concat(other.Integrations), StringComparer.Ordinal),
			new HashSet<(string, string)>(Inboxes.Concat(other.Inboxes)));
	}
}

public interface IEmailSyncSignal
{
	void Request(string integrationId, string? inboxId);

	void RequestAll();

	EmailSyncRequest TakePending();

	Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken);
}

public sealed class EmailSyncSignal : IEmailSyncSignal, IDisposable
{
	private readonly SemaphoreSlim _pending = new(0, 1);
	private readonly Lock _gate = new();
	private readonly HashSet<string> _integrations = new(StringComparer.Ordinal);
	private readonly HashSet<(string, string)> _inboxes = [];
	private bool _all;

	public void Request(string integrationId, string? inboxId)
	{
		if (string.IsNullOrEmpty(integrationId))
		{
			return;
		}

		lock (_gate)
		{
			if (inboxId is null)
			{
				_integrations.Add(integrationId);
			}
			else
			{
				_inboxes.Add((integrationId, inboxId));
			}
		}

		Release();
	}

	public void RequestAll()
	{
		lock (_gate)
		{
			_all = true;
		}

		Release();
	}

	public EmailSyncRequest TakePending()
	{
		lock (_gate)
		{
			var request = new EmailSyncRequest(_all,
				new HashSet<string>(_integrations, StringComparer.Ordinal),
				new HashSet<(string, string)>(_inboxes));
			_all = false;
			_integrations.Clear();
			_inboxes.Clear();
			return request;
		}
	}

	public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var delay = Task.Delay(timeout, time, linked.Token);
		var signalled = _pending.WaitAsync(linked.Token);

		var first = await Task.WhenAny(delay, signalled).ConfigureAwait(false);
		await linked.CancelAsync().ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		return first == signalled && signalled.IsCompletedSuccessfully;
	}

	public void Dispose()
	{
		_pending.Dispose();
	}

	private void Release()
	{
		try
		{
			_pending.Release();
		}
		catch (SemaphoreFullException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
	}
}
