using MacroDeckHost.Application.Store;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public static class StoreRegistryRefreshRunBodyFactory
{
	public static StoreRegistryRefreshRunBody? Create(StoreRegistryRefreshRun? run) => run is null
		? null
		: new StoreRegistryRefreshRunBody
		{
			HostInstanceId = run.HostInstanceId,
			Id = run.Id,
			Revision = run.Revision,
			Trigger = run.Trigger,
			State = run.State,
			StartedAt = run.StartedAt,
			FinishedAt = run.FinishedAt,
			FilesCompleted = run.FilesCompleted,
			FilesTotal = run.FilesTotal,
			Entries = run.Entries.Select(entry => new StoreRegistryRefreshLogEntryBody
				{
					At = entry.At,
					Step = entry.Step,
					Count = entry.Count,
					Sequence = entry.Sequence,
					Error = entry.Error is { } error ? ErrorCode(error) : null,
					Detail = entry.Detail
				})
				.ToList()
		};

	public static string ErrorCode(RegistryRefreshError error) => error switch
	{
		RegistryRefreshError.Disabled => "disabled",
		RegistryRefreshError.NetworkFailure => "network_failure",
		RegistryRefreshError.Malformed => "malformed",
		RegistryRefreshError.BudgetExceeded => "budget_exceeded",
		RegistryRefreshError.SizeMismatch => "size_mismatch",
		RegistryRefreshError.SequenceRollback => "sequence_rollback",
		RegistryRefreshError.SignatureInvalid => "signature_invalid",
		RegistryRefreshError.CertificateUntrusted => "certificate_untrusted",
		RegistryRefreshError.SigningKeyRevoked => "signing_key_revoked",
		RegistryRefreshError.SnapshotUnchanged => "snapshot_unchanged",
		RegistryRefreshError.StorageFailure => "storage_failure",
		_ => "failed"
	};
}
