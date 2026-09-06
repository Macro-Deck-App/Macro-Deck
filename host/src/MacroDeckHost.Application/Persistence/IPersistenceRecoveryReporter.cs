namespace MacroDeckHost.Application.Persistence;

public enum PersistenceRecoverySource
{
	PendingWrite,
	Backup,
}

public sealed record PersistenceRecovery(
	string DataKind,
	string Path,
	PersistenceRecoverySource Source,
	string? PreservedCorruptPath);

public sealed record PersistenceLoss(string DataKind, string Path);

public interface IPersistenceRecoveryReporter
{
	void ReportRecovered(PersistenceRecovery recovery);

	void ReportUnrecoverable(PersistenceLoss loss);
}
