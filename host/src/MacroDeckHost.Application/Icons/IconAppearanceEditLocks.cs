namespace MacroDeckHost.Application.Icons;

public sealed class IconAppearanceEditLocks
{
	private readonly Dictionary<Guid, Gate> _gates = new();
	private readonly Lock _sync = new();

	public async Task<IAsyncDisposable> Acquire(IEnumerable<Guid> iconIds, CancellationToken cancellationToken)
	{
		var held = new List<Guid>();
		try
		{
			foreach (var iconId in iconIds.Distinct().Order())
			{
				var gate = Retain(iconId);
				try
				{
					await gate.Semaphore.WaitAsync(cancellationToken);
				}
				catch
				{
					Forget(iconId);
					throw;
				}

				held.Add(iconId);
			}
		}
		catch
		{
			Release(held);
			throw;
		}

		return new Lease(this, held);
	}

	private Gate Retain(Guid iconId)
	{
		lock (_sync)
		{
			if (!_gates.TryGetValue(iconId, out var gate))
			{
				gate = new Gate();
				_gates[iconId] = gate;
			}

			gate.Users++;
			return gate;
		}
	}

	private void Forget(Guid iconId)
	{
		lock (_sync)
		{
			var gate = _gates[iconId];
			if (--gate.Users == 0)
			{
				_gates.Remove(iconId);
				gate.Semaphore.Dispose();
			}
		}
	}

	private void Release(List<Guid> iconIds)
	{
		for (var index = iconIds.Count - 1; index >= 0; index--)
		{
			Gate gate;
			lock (_sync)
			{
				gate = _gates[iconIds[index]];
			}

			gate.Semaphore.Release();
			Forget(iconIds[index]);
		}
	}

	private sealed class Gate
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);

		public int Users { get; set; }
	}

	private sealed class Lease(IconAppearanceEditLocks owner, List<Guid> iconIds) : IAsyncDisposable
	{
		private int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				owner.Release(iconIds);
			}

			return ValueTask.CompletedTask;
		}
	}
}
