using MacroDeckHost.Application.Variables.Files;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class FakeVariableFileSystem : IVariableFileSystem
{
	private readonly object _lock = new();
	private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<Action>> _watchers = new(StringComparer.Ordinal);
	private readonly List<(string Path, string Content)> _writes = [];

	public TaskCompletionSource? ReadGate { get; set; }

	public int GatedReads { get; private set; }

	public int GatedWrites { get; private set; }

	public TaskCompletionSource? WriteGate { get; set; }

	public Func<string, Exception?>? WriteFailure { get; set; }

	public IReadOnlyList<(string Path, string Content)> Writes
	{
		get
		{
			lock (_lock)
			{
				return _writes.ToList();
			}
		}
	}

	public string? Content(string path)
	{
		lock (_lock)
		{
			return _files.TryGetValue(path, out var content) ? content : null;
		}
	}

	public int WatcherCount(string path)
	{
		lock (_lock)
		{
			return _watchers.TryGetValue(path, out var list) ? list.Count : 0;
		}
	}

	public void SetFile(string path, string content, bool notify = true)
	{
		lock (_lock)
		{
			_files[path] = content;
		}

		if (notify)
		{
			Notify(path);
		}
	}

	public void DeleteFile(string path)
	{
		lock (_lock)
		{
			_files.Remove(path);
		}

		Notify(path);
	}

	public void Notify(string path)
	{
		Action[] watchers;
		lock (_lock)
		{
			watchers = _watchers.TryGetValue(path, out var list) ? list.ToArray() : [];
		}

		foreach (var watcher in watchers)
		{
			watcher();
		}
	}

	public async Task<FileReadOutcome> ReadAsync(string path, CancellationToken cancellationToken = default)
	{
		string? content;
		TaskCompletionSource? gate;
		lock (_lock)
		{
			content = _files.TryGetValue(path, out var value) ? value : null;
			gate = ReadGate;
			if (gate is not null)
			{
				GatedReads++;
			}
		}

		if (gate is not null)
		{
			await gate.Task;
		}

		return new FileReadOutcome(content);
	}

	public async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
	{
		if (WriteGate is { } gate)
		{
			lock (_lock)
			{
				GatedWrites++;
			}

			await gate.Task;
		}

		if (WriteFailure?.Invoke(path) is { } failure)
		{
			throw failure;
		}

		lock (_lock)
		{
			_files[path] = content;
			_writes.Add((path, content));
		}

		Notify(path);
	}

	public IDisposable Watch(string path, Action changed)
	{
		lock (_lock)
		{
			if (!_watchers.TryGetValue(path, out var list))
			{
				list = [];
				_watchers[path] = list;
			}

			list.Add(changed);
		}

		return new Unwatch(this, path, changed);
	}

	private sealed class Unwatch(FakeVariableFileSystem owner, string path, Action changed) : IDisposable
	{
		public void Dispose()
		{
			lock (owner._lock)
			{
				if (owner._watchers.TryGetValue(path, out var list))
				{
					list.Remove(changed);
				}
			}
		}
	}
}
