namespace MacroDeckHost.Application.Backups;

public static class BackupArchiveLimits
{
	public const int MaxOuterEntries = 8;

	public const int MaxManifestBytes = 256 * 1024;

	public const long MaxContentIndexBytes = 64L << 20;

	public const long MaxPayloadBytes = 8L << 30;

	public const int MaxInnerEntries = 200_000;

	public const long MaxSingleEntryBytes = 2L << 30;

	public const long MaxTotalPlaintextBytes = 8L << 30;

	public static bool IsSafeEntryPath(string path)
	{
		if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains('\\'))
		{
			return false;
		}

		foreach (var segment in path.Split('/'))
		{
			if (segment is "" or "." or ".." || segment.Contains(':'))
			{
				return false;
			}
		}

		return true;
	}
}
