using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Integrations.Ping.Connections;

internal interface IPingTargets
{
	event EventHandler? Changed;

	IReadOnlyList<PingSnapshot> Targets { get; }

	PingSnapshot? Find(string? entryId);
}

internal sealed class PingHub : IPingTargets
{
	private readonly Lock _sync = new();

	private PingSnapshot[] _targets = [];

	public event EventHandler? Changed;

	public IReadOnlyList<PingSnapshot> Targets
	{
		get
		{
			lock (_sync)
			{
				return _targets;
			}
		}
	}

	public PingSnapshot? Find(string? entryId)
	{
		return Guid.TryParse(entryId, out var id) ? Targets.FirstOrDefault(target => target.EntryId == id) : null;
	}

	public void Replace(IReadOnlyList<PingSnapshot> snapshots)
	{
		ArgumentNullException.ThrowIfNull(snapshots);

		lock (_sync)
		{
			if (_targets.SequenceEqual(snapshots))
			{
				return;
			}

			_targets = [.. snapshots];
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Update(PingSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		lock (_sync)
		{
			var index = Array.FindIndex(_targets, target => target.EntryId == snapshot.EntryId);
			if (index < 0 || _targets[index] == snapshot)
			{
				return;
			}

			var next = (PingSnapshot[])_targets.Clone();
			next[index] = snapshot;
			_targets = next;
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Announce()
	{
		Changed?.Invoke(this, EventArgs.Empty);
	}
}
