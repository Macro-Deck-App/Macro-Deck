using System.Collections.Concurrent;
using MacroDeck.Sdk.Colors;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IColorApi" />. Seed what a value resolves to with <see cref="Set" />; an unseeded
/// fixed colour resolves to its canonical form and an unseeded reference to <c>null</c>, as on a Macro Deck
/// where the variable does not exist. A watch receives its current value once, then every change
/// <see cref="Set" /> makes to it, until it is disposed. Callbacks run before <see cref="Set" /> returns.
/// </summary>
public sealed class FakeColorApi : IColorApi
{
	private readonly ConcurrentDictionary<(string Value, string? WidgetId), string?> _seeded = new();
	private readonly ConcurrentDictionary<Watch, byte> _watches = new();

	/// <summary>The watches currently active.</summary>
	public int ActiveWatchCount => _watches.Count;

	/// <summary>Makes <paramref name="value" /> resolve to <paramref name="color" /> and notifies the watches
	/// whose value changed.</summary>
	public async Task Set(string value, string? color, string? widgetId = null)
	{
		_seeded[(value, widgetId)] = color;

		foreach (var watch in _watches.Keys.Where(watch => watch.Value == value && watch.WidgetId == widgetId))
		{
			await watch.DeliverAsync(color).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(value, widgetId));

	/// <inheritdoc />
	public async Task<IAsyncDisposable> WatchAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(onChanged);

		var watch = new Watch(this, value, widgetId, onChanged);
		_watches[watch] = 0;
		await watch.DeliverAsync(Resolve(value, widgetId)).ConfigureAwait(false);
		return watch;
	}

	private string? Resolve(string value, string? widgetId)
		=> _seeded.TryGetValue((value, widgetId), out var color) ? color : LocalColorApi.Resolve(value);

	private sealed class Watch(FakeColorApi owner, string value, string? widgetId,
		Func<string?, CancellationToken, Task> onChanged) : IAsyncDisposable
	{
		private bool _delivered;
		private string? _last;

		public string Value { get; } = value;

		public string? WidgetId { get; } = widgetId;

		public async Task DeliverAsync(string? color)
		{
			if (_delivered && string.Equals(color, _last, StringComparison.Ordinal))
			{
				return;
			}

			_delivered = true;
			_last = color;
			await onChanged(color, CancellationToken.None).ConfigureAwait(false);
		}

		public ValueTask DisposeAsync()
		{
			owner._watches.TryRemove(this, out _);
			return ValueTask.CompletedTask;
		}
	}
}
