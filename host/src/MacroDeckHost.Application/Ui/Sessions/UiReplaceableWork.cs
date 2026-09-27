using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class UiReplaceableWork<TItem>
{
	private readonly Channel<Slot> _items = Channel.CreateUnbounded<Slot>(new UnboundedChannelOptions
	{
		SingleReader = true, AllowSynchronousContinuations = false
	});

	private readonly Lock _gate = new();
	private readonly Dictionary<string, Slot> _open = new(StringComparer.Ordinal);

	public bool Write(TItem item)
	{
		lock (_gate)
		{
			_open.Clear();

			return _items.Writer.TryWrite(new Slot(null, item));
		}
	}

	public bool WriteReplaceable(string key, TItem item)
	{
		lock (_gate)
		{
			if (_open.TryGetValue(key, out var open))
			{
				open.Item = item;

				return true;
			}

			var slot = new Slot(key, item);

			if (!_items.Writer.TryWrite(slot))
			{
				return false;
			}

			_open[key] = slot;

			return true;
		}
	}

	public void Complete() => _items.Writer.TryComplete();

	public async IAsyncEnumerable<TItem> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var slot in _items.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
		{
			yield return Take(slot);
		}
	}

	private TItem Take(Slot slot)
	{
		lock (_gate)
		{
			if (slot.Key is { } key && _open.TryGetValue(key, out var open) && ReferenceEquals(open, slot))
			{
				_open.Remove(key);
			}

			return slot.Item;
		}
	}

	private sealed class Slot(string? key, TItem item)
	{
		public string? Key { get; } = key;

		public TItem Item { get; set; } = item;
	}
}
