using Serilog;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

internal sealed class AutoHotkeyRunFolders
{
	private const string LockFileName = ".lock";

	private static readonly TimeSpan _unlockedGrace = TimeSpan.FromMinutes(1);

	private readonly ILogger _logger;

	public AutoHotkeyRunFolders(string root, ILogger logger)
	{
		Root = root;
		_logger = logger;
	}

	public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "MacroDeck", "autohotkey");

	public string Root { get; }

	public AutoHotkeyRunFolder Create(Guid runId)
	{
		var path = Path.Combine(Root, runId.ToString("N"));
		Directory.CreateDirectory(path);
		var lockFile = new FileStream(Path.Combine(path, LockFileName),
			FileMode.Create,
			FileAccess.ReadWrite,
			FileShare.None);
		return new AutoHotkeyRunFolder(path, lockFile, _logger);
	}

	// Several hosts can share the temp root; a folder whose lock file is still held belongs to a run of
	// another host and is left alone.
	public int DeleteStale()
	{
		if (!Directory.Exists(Root))
		{
			return 0;
		}

		var deleted = 0;
		foreach (var folder in Directory.EnumerateDirectories(Root))
		{
			if (TryAcquire(folder) is not { } acquired)
			{
				continue;
			}

			acquired.Dispose();
			if (AutoHotkeyRunFolder.TryDelete(folder, _logger))
			{
				deleted++;
			}
		}

		return deleted;
	}

	private static Holder? TryAcquire(string folder)
	{
		var lockPath = Path.Combine(folder, LockFileName);
		try
		{
			if (File.Exists(lockPath))
			{
				return new Holder(new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
			}

			// A folder another host has just created may not have its lock file yet.
			return DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) > _unlockedGrace ? new Holder(null) : null;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private sealed class Holder : IDisposable
	{
		private readonly FileStream? _stream;

		public Holder(FileStream? stream)
		{
			_stream = stream;
		}

		public void Dispose()
		{
			_stream?.Dispose();
		}
	}
}

internal sealed class AutoHotkeyRunFolder : IDisposable
{
	private readonly FileStream _lock;
	private readonly ILogger _logger;
	private int _disposed;

	public AutoHotkeyRunFolder(string path, FileStream lockFile, ILogger logger)
	{
		FullPath = path;
		_lock = lockFile;
		_logger = logger;
	}

	public string FullPath { get; }

	public string PathOf(string name)
	{
		return Path.Combine(FullPath, name);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		_lock.Dispose();
		TryDelete(FullPath, _logger);
	}

	public static bool TryDelete(string path, ILogger logger)
	{
		try
		{
			Directory.Delete(path, true);
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.Debug(ex, "Could not delete the AutoHotkey run folder yet");
			return false;
		}
	}
}
