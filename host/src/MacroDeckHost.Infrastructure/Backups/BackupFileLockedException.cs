namespace MacroDeckHost.Infrastructure.Backups;

/// <summary>
/// A file the backup had to read was held by another process. Unlike a bare <see cref="IOException" />
/// this carries the path, so the failure can name the file the user has to release.
/// </summary>
public sealed class BackupFileLockedException : IOException
{
	// Windows reports a denied share mode as ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION; both
	// arrive as an IOException whose HResult carries the Win32 code in its low word.
	private const int SharingViolation = 32;
	private const int LockViolation = 33;

	public BackupFileLockedException(string path, Exception? inner = null)
		: base($"The file '{path}' could not be read because another program is using it.", inner)
		=> Path = path;

	public string Path { get; }

	public static bool IsLockViolation(IOException exception)
		=> (exception.HResult & 0xFFFF) is SharingViolation or LockViolation;
}
