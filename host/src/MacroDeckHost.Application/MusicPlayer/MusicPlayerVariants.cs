using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed record MusicPlayerVariant
{
	private MusicPlayerVariant(string instanceId, IReadOnlyDictionary<string, object> options, string key)
	{
		InstanceId = instanceId;
		Options = options;
		Key = key;
	}

	public string InstanceId { get; }

	public IReadOnlyDictionary<string, object> Options { get; }

	public string Key { get; }

	public static MusicPlayerVariant? For(
		MusicPlayerInstanceDescriptor instance,
		IReadOnlyDictionary<string, JsonElement>? stored)
	{
		ArgumentNullException.ThrowIfNull(instance);

		return instance.Options.Count == 0
			? null
			: Create(instance.InstanceId, MusicPlayerOptionValues.Normalize(instance.Options, stored));
	}

	public static MusicPlayerVariant Create(string instanceId, IReadOnlyDictionary<string, object> options)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);
		ArgumentNullException.ThrowIfNull(options);

		var key = new StringBuilder(instanceId);

		foreach (var (name, value) in options.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			key.Append('|').Append(name).Append('=').Append(JsonSerializer.Serialize(value));
		}

		return new MusicPlayerVariant(instanceId, options, key.ToString());
	}

	public bool Equals(MusicPlayerVariant? other) => other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}

public interface IMusicPlayerVariants
{
	event EventHandler<MusicPlayerVariant>? Released;

	IDisposable Acquire(MusicPlayerVariant variant);

	IReadOnlyList<MusicPlayerVariant> Demanded();

	MusicPlayerStatePayload? GetState(string key);

	bool Record(string key, MusicPlayerStatePayload payload);

	// One tick of grace: an editor preview reopens its session on every keystroke and must find its state.
	IReadOnlyList<MusicPlayerVariant> Sweep();
}

public sealed class MusicPlayerVariants : IMusicPlayerVariants
{
	private readonly IMusicPlayerPollNudge _pollNudge;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	public MusicPlayerVariants(IMusicPlayerPollNudge pollNudge)
	{
		_pollNudge = pollNudge;
	}

	public event EventHandler<MusicPlayerVariant>? Released;

	public IDisposable Acquire(MusicPlayerVariant variant)
	{
		ArgumentNullException.ThrowIfNull(variant);

		bool isNew;

		lock (_sync)
		{
			isNew = !_entries.TryGetValue(variant.Key, out var entry);
			if (isNew)
			{
				entry = new Entry(variant);
				_entries[variant.Key] = entry;
			}

			entry!.Holders++;
			entry.IdleSweeps = 0;
		}

		if (isNew)
		{
			_pollNudge.Nudge();
		}

		return new Handle(this, variant.Key);
	}

	public IReadOnlyList<MusicPlayerVariant> Demanded()
	{
		lock (_sync)
		{
			return [.. _entries.Values.Where(entry => entry.Holders > 0).Select(entry => entry.Variant)];
		}
	}

	public MusicPlayerStatePayload? GetState(string key)
	{
		lock (_sync)
		{
			return _entries.GetValueOrDefault(key)?.State;
		}
	}

	public bool Record(string key, MusicPlayerStatePayload payload)
	{
		ArgumentNullException.ThrowIfNull(payload);

		lock (_sync)
		{
			if (!_entries.TryGetValue(key, out var entry))
			{
				return false;
			}

			var json = JsonSerializer.Serialize(payload);
			if (string.Equals(entry.StateJson, json, StringComparison.Ordinal))
			{
				return false;
			}

			entry.State = payload;
			entry.StateJson = json;

			return true;
		}
	}

	public IReadOnlyList<MusicPlayerVariant> Sweep()
	{
		List<MusicPlayerVariant> released = [];

		lock (_sync)
		{
			foreach (var entry in _entries.Values.Where(entry => entry.Holders == 0).ToList())
			{
				if (++entry.IdleSweeps > 1)
				{
					_entries.Remove(entry.Variant.Key);
					released.Add(entry.Variant);
				}
			}
		}

		foreach (var variant in released)
		{
			Released?.Invoke(this, variant);
		}

		return released;
	}

	private void Release(string key)
	{
		lock (_sync)
		{
			if (_entries.TryGetValue(key, out var entry) && entry.Holders > 0)
			{
				entry.Holders--;
			}
		}
	}

	private sealed class Entry(MusicPlayerVariant variant)
	{
		public MusicPlayerVariant Variant { get; } = variant;

		public int Holders { get; set; }

		public int IdleSweeps { get; set; }

		public MusicPlayerStatePayload? State { get; set; }

		public string? StateJson { get; set; }
	}

	private sealed class Handle(MusicPlayerVariants owner, string key) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				owner.Release(key);
			}
		}
	}
}
