// ReSharper disable CheckNamespace

using System.Text;

namespace MacroDeck.Sdk.Storage;

// Linked into the host and into MacroDeck.Plugin.Hosting so in-process and out-of-process storage share
// one file protocol: the same one the host's own durable JSON documents use.
internal sealed class DirectoryIntegrationStorage : IIntegrationStorage, IDisposable
{
	internal const string TempSuffix = ".tmp";
	internal const string BackupSuffix = ".bak";

	private static readonly UTF8Encoding _utf8 = new(false);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Action<string, string>? _beforeAccess;

	public DirectoryIntegrationStorage(string directory, Action<string, string>? beforeAccess = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(directory);

		Directory = directory;
		_beforeAccess = beforeAccess;
	}

	public string Directory { get; }

	public string PathFor(string key)
	{
		return Path.Combine(Directory, key);
	}

	public async Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var path = PathFor(key);
			_beforeAccess?.Invoke(key, path);

			var anyExists = false;
			foreach (var candidate in new[] { path, path + TempSuffix, path + BackupSuffix })
			{
				if (!File.Exists(candidate))
				{
					continue;
				}

				anyExists = true;
				if (TryRead(candidate) is { Length: > 0 } content && (isValid is null || Accepts(isValid, content)))
				{
					return IntegrationStorageReadResult.Found(content);
				}
			}

			return anyExists ? IntegrationStorageReadResult.Unreadable : IntegrationStorageReadResult.Missing;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		ArgumentException.ThrowIfNullOrEmpty(content);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var path = PathFor(key);
			_beforeAccess?.Invoke(key, path);
			System.IO.Directory.CreateDirectory(Directory);

			var tempPath = path + TempSuffix;
			var bytes = _utf8.GetBytes(content);
			using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes, 0, bytes.Length);

				// The bytes must reach storage before the rename publishes them, or a power loss can leave an
				// empty document behind.
				stream.Flush(true);
			}

			try
			{
				if (File.Exists(path))
				{
					File.Move(path, path + BackupSuffix, true);
				}

				File.Move(tempPath, path, true);
			}
			catch
			{
				TryDelete(tempPath);
				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var path = PathFor(key);
			_beforeAccess?.Invoke(key, path);
			if (!System.IO.Directory.Exists(Directory))
			{
				return;
			}

			File.Delete(path);
			File.Delete(path + TempSuffix);
			File.Delete(path + BackupSuffix);
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	private static string? TryRead(string path)
	{
		try
		{
			return File.ReadAllText(path, Encoding.UTF8);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool Accepts(Func<string, bool> isValid, string content)
	{
		try
		{
			return isValid(content);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return false;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
