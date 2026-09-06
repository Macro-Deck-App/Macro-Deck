namespace MacroDeckHost.Application.Backups;

public static class BackupFileNames
{
	public const string Extension = ".macroDeckBackup";

	public const string IncompleteExtension = ".macroDeckBackup.part";

	public const string ManifestEntry = "manifest.json";

	public const string PayloadEntry = "payload.enc";

	public const string ContentIndexEntry = "content.json";

	public const string DatabaseEntry = "db/database.db";

	public const string FilesPrefix = "files/";
}
