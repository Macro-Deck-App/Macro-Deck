using MacroDeckHost.Application.Connect;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

/// <summary>
/// Serialises credential writes onto a single FIFO tail so a rotation can never overtake the rotation
/// before it. A queued write is never abandoned or cancelled: dropping a rotation mid-write is what
/// loses a refresh token permanently.
/// </summary>
public sealed class ConnectTokenPersister : IDisposable
{
	private readonly IConnectCredentialStore _store;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();

	private Task _tail = Task.CompletedTask;
	private bool _accepting = true;

	public ConnectTokenPersister(IConnectCredentialStore store, ILogger logger)
	{
		_store = store;
		_logger = logger.ForContext<ConnectTokenPersister>();
	}

	public Task<bool> EnqueueAndWaitAsync(ConnectCredential credential)
	{
		lock (_sync)
		{
			if (!_accepting)
			{
				_logger.Warning("Ignoring a Macro Deck Connect credential rotation after persistence stopped");
				return Task.FromResult(false);
			}

			var mine = PersistAfterAsync(_tail, credential);
			_tail = mine;
			return mine;
		}
	}

	public async Task FlushAsync()
	{
		Task tail;
		lock (_sync)
		{
			tail = _tail;
		}

		await tail;
	}

	public async Task CompleteAsync()
	{
		Task tail;
		lock (_sync)
		{
			_accepting = false;
			tail = _tail;
		}

		await tail;
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_accepting = false;
		}
	}

	private async Task<bool> PersistAfterAsync(Task predecessor, ConnectCredential credential)
	{
		await predecessor;

		try
		{
			await _store.Save(credential, CancellationToken.None);
			return true;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to persist the rotated Macro Deck Connect credential");
			return false;
		}
	}
}
