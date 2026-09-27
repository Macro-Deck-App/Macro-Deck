using System.Text;
using MacroDeckHost.Application.Variables.Files;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Variables;

public sealed class VariableFileSystem : IVariableFileSystem, IDisposable
{
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan MissingDirectoryPoll = TimeSpan.FromSeconds(2);

	private static readonly StringComparer FileNameComparer = OperatingSystem.IsLinux()
		? StringComparer.Ordinal
		: StringComparer.OrdinalIgnoreCase;

	private readonly object _lock = new();
	private readonly Dictionary<string, DirectoryWatch> _directories = new(FileNameComparer);
	private readonly ILogger _logger;

	public VariableFileSystem(ILogger logger)
	{
		_logger = logger;
	}

	// A FIFO, a device or an unreachable share can block a read indefinitely, so the caller stops waiting
	// after the timeout and the read finishes, or stays blocked, on its own.
	public async Task<FileReadOutcome> ReadAsync(string path, CancellationToken cancellationToken = default)
	{
		if (!Path.IsPathFullyQualified(path))
		{
			return FileReadOutcome.Unavailable;
		}

		var read = Task.Run(() => Read(path), CancellationToken.None);
		var finished = await Task.WhenAny(read, Task.Delay(ReadTimeout, cancellationToken));
		return finished == read ? await read : FileReadOutcome.Unavailable;
	}

	public Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
		=> VariableFileText.WriteAsync(path, content, cancellationToken);

	public IDisposable Watch(string path, Action changed)
	{
		if (!Path.IsPathFullyQualified(path) ||
			Path.GetDirectoryName(path) is not { Length: > 0 } directory ||
			Path.GetFileName(path) is not { Length: > 0 } fileName)
		{
			return new Subscription(() => { });
		}

		DirectoryWatch watch;
		lock (_lock)
		{
			if (!_directories.TryGetValue(directory, out watch!))
			{
				watch = new DirectoryWatch(directory, _logger);
				_directories[directory] = watch;
			}

			watch.Add(fileName, changed);
		}

		return new Subscription(() =>
		{
			lock (_lock)
			{
				if (watch.Remove(fileName, changed))
				{
					_directories.Remove(directory);
					watch.Dispose();
				}
			}
		});
	}

	public void Dispose()
	{
		lock (_lock)
		{
			foreach (var watch in _directories.Values)
			{
				watch.Dispose();
			}

			_directories.Clear();
		}
	}

	private static FileReadOutcome Read(string path)
	{
		for (var attempt = 0;; attempt++)
		{
			try
			{
				if (!File.Exists(path))
				{
					return FileReadOutcome.Unavailable;
				}

				using var stream = new FileStream(path,
					FileMode.Open,
					FileAccess.Read,
					FileShare.ReadWrite | FileShare.Delete);
				var buffer = new byte[VariableFileText.MaxBytes + 1];
				var length = 0;
				int count;
				while (length < buffer.Length && (count = stream.Read(buffer, length, buffer.Length - length)) > 0)
				{
					length += count;
				}

				if (length > VariableFileText.MaxBytes)
				{
					return FileReadOutcome.Unavailable;
				}

				using var reader = new StreamReader(new MemoryStream(buffer, 0, length), Encoding.UTF8, true);
				return new FileReadOutcome(reader.ReadToEnd());
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
				or UnauthorizedAccessException)
			{
				return FileReadOutcome.Unavailable;
			}
			catch (IOException) when (attempt < 3)
			{
				// Windows reports a sharing violation while the writing application still holds the file.
				Thread.Sleep(50);
			}
			catch (IOException)
			{
				return FileReadOutcome.Unavailable;
			}
		}
	}

	private sealed class Subscription(Action dispose) : IDisposable
	{
		private Action? _dispose = dispose;

		public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
	}

	private sealed class DirectoryWatch : IDisposable
	{
		private readonly string _directory;
		private readonly ILogger _logger;
		private readonly object _lock = new();
		private readonly Dictionary<string, List<Action>> _subscribers = new(FileNameComparer);
		private FileSystemWatcher? _watcher;
		private Timer? _poll;
		private bool _disposed;

		public DirectoryWatch(string directory, ILogger logger)
		{
			_directory = directory;
			_logger = logger;
			lock (_lock)
			{
				Start();
			}
		}

		public void Add(string fileName, Action changed)
		{
			lock (_lock)
			{
				if (!_subscribers.TryGetValue(fileName, out var list))
				{
					list = [];
					_subscribers[fileName] = list;
				}

				list.Add(changed);
			}
		}

		public bool Remove(string fileName, Action changed)
		{
			lock (_lock)
			{
				if (_subscribers.TryGetValue(fileName, out var list))
				{
					list.Remove(changed);
					if (list.Count == 0)
					{
						_subscribers.Remove(fileName);
					}
				}

				return _subscribers.Count == 0;
			}
		}

		public void Dispose()
		{
			lock (_lock)
			{
				_disposed = true;
				Stop();
			}
		}

		private void Start()
		{
			if (_disposed)
			{
				return;
			}

			if (!Directory.Exists(_directory))
			{
				_poll ??= new Timer(_ => OnPoll(), null, MissingDirectoryPoll, MissingDirectoryPoll);
				return;
			}

			try
			{
				var watcher = new FileSystemWatcher(_directory)
				{
					IncludeSubdirectories = false,
					NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
						NotifyFilters.CreationTime
				};
				watcher.Changed += (_, e) => Notify(e.Name);
				watcher.Created += (_, e) => Notify(e.Name);
				watcher.Deleted += (_, e) => OnDeleted(e.Name);
				watcher.Renamed += (_, e) =>
				{
					Notify(e.OldName);
					Notify(e.Name);
				};
				watcher.Error += (_, e) => OnError(e.GetException());
				watcher.EnableRaisingEvents = true;
				_watcher = watcher;
			}
			catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
			{
				_logger.Warning(ex, "Could not watch {Directory} for variable files", _directory);
				_poll ??= new Timer(_ => OnPoll(), null, MissingDirectoryPoll, MissingDirectoryPoll);
			}
		}

		private void Stop()
		{
			_watcher?.Dispose();
			_watcher = null;
			_poll?.Dispose();
			_poll = null;
		}

		private void Restart()
		{
			lock (_lock)
			{
				Stop();
				Start();
			}

			NotifyAll();
		}

		private void OnPoll()
		{
			lock (_lock)
			{
				if (_disposed || _watcher is not null || !Directory.Exists(_directory))
				{
					return;
				}
			}

			Restart();
		}

		private void OnDeleted(string? name)
		{
			if (!Directory.Exists(_directory))
			{
				Restart();
				return;
			}

			Notify(name);
		}

		private void OnError(Exception exception)
		{
			_logger.Debug(exception, "Watching {Directory} for variable files failed; restarting", _directory);
			Restart();
		}

		private void Notify(string? name)
		{
			if (name is null)
			{
				return;
			}

			Action[] subscribers;
			lock (_lock)
			{
				if (_disposed || !_subscribers.TryGetValue(name, out var list))
				{
					return;
				}

				subscribers = list.ToArray();
			}

			Invoke(subscribers);
		}

		private void NotifyAll()
		{
			Action[] subscribers;
			lock (_lock)
			{
				if (_disposed)
				{
					return;
				}

				subscribers = _subscribers.Values.SelectMany(list => list).ToArray();
			}

			Invoke(subscribers);
		}

		private void Invoke(Action[] subscribers)
		{
			foreach (var subscriber in subscribers)
			{
				try
				{
					subscriber();
				}
				catch (Exception ex)
				{
					_logger.Warning(ex, "A variable file change handler failed");
				}
			}
		}
	}
}
