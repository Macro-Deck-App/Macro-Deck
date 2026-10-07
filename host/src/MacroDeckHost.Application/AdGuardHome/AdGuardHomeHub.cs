namespace MacroDeckHost.Application.AdGuardHome;

public delegate Task<AdGuardHomeCommandOutcome> AdGuardHomeCommandExecutor(
	string entryId,
	AdGuardHomeCommand command,
	CancellationToken cancellationToken);

public interface IAdGuardHomeInstances
{
	event EventHandler? Changed;

	IReadOnlyList<AdGuardHomeSnapshot> Instances { get; }

	AdGuardHomeSnapshot? Find(string? entryId);

	Task<AdGuardHomeCommandOutcome> ExecuteAsync(
		string entryId,
		AdGuardHomeCommand command,
		CancellationToken cancellationToken);
}

public interface IAdGuardHomeSink
{
	void Replace(IReadOnlyList<AdGuardHomeSnapshot> snapshots);

	void Update(AdGuardHomeSnapshot snapshot);

	void UseExecutor(AdGuardHomeCommandExecutor? executor);
}

public interface IAdGuardHomeSinkConsumer
{
	void UseAdGuardHomeSink(IAdGuardHomeSink sink);
}

public sealed class AdGuardHomeHub : IAdGuardHomeInstances, IAdGuardHomeSink
{
	private readonly Lock _sync = new();

	private AdGuardHomeSnapshot[] _instances = [];
	private AdGuardHomeCommandExecutor? _executor;

	public event EventHandler? Changed;

	public IReadOnlyList<AdGuardHomeSnapshot> Instances
	{
		get
		{
			lock (_sync)
			{
				return _instances;
			}
		}
	}

	public AdGuardHomeSnapshot? Find(string? entryId)
		=> string.IsNullOrEmpty(entryId)
			? null
			: Instances.FirstOrDefault(instance => string.Equals(instance.EntryId, entryId, StringComparison.Ordinal));

	public Task<AdGuardHomeCommandOutcome> ExecuteAsync(
		string entryId,
		AdGuardHomeCommand command,
		CancellationToken cancellationToken)
	{
		AdGuardHomeCommandExecutor? executor;
		lock (_sync)
		{
			executor = _executor;
		}

		return executor is null
			? Task.FromResult(AdGuardHomeCommandOutcome.NotFound)
			: executor(entryId, command, cancellationToken);
	}

	public void Replace(IReadOnlyList<AdGuardHomeSnapshot> snapshots)
	{
		ArgumentNullException.ThrowIfNull(snapshots);

		lock (_sync)
		{
			if (_instances.SequenceEqual(snapshots))
			{
				return;
			}

			_instances = [.. snapshots];
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Update(AdGuardHomeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		lock (_sync)
		{
			var index = -1;
			for (var i = 0; i < _instances.Length; i++)
			{
				if (string.Equals(_instances[i].EntryId, snapshot.EntryId, StringComparison.Ordinal))
				{
					index = i;
					break;
				}
			}

			if (index < 0 || _instances[index] == snapshot)
			{
				return;
			}

			var next = (AdGuardHomeSnapshot[])_instances.Clone();
			next[index] = snapshot;
			_instances = next;
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void UseExecutor(AdGuardHomeCommandExecutor? executor)
	{
		lock (_sync)
		{
			_executor = executor;
		}
	}
}
