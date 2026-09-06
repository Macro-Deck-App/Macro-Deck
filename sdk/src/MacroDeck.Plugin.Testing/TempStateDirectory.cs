namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A fresh, uniquely named directory for one self-registering plugin's persisted state, deleted on
/// dispose.
///
/// <para>
/// Without one, a self-registering plugin under test falls back to the real per-user state directory
/// (<c>FilePluginCredentialStore</c>'s default) and would read and write actual credentials on the
/// machine running the tests. Two instances of this type never share a directory, which is what makes
/// two plugins under test - or two starts of the same one - independently verifiable.
/// </para>
/// </summary>
public sealed class TempStateDirectory : IDisposable
{
	private bool _disposed;

	/// <summary>Creates and reserves a new, empty directory.</summary>
	public TempStateDirectory()
		=> Path = Directory.CreateTempSubdirectory("macrodeck-plugin-test-state-").FullName;

	/// <summary>The directory's full path.</summary>
	public string Path { get; }

	/// <summary>Deletes the directory and everything under it.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
		catch (IOException)
		{
			// Best effort: a file still open under the directory (a credential file mid-write on a slow
			// disposal race) must not turn test cleanup into a test failure.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
