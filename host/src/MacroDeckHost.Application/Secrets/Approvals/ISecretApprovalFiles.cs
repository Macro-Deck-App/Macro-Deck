namespace MacroDeckHost.Application.Secrets.Approvals;

public enum SecretApprovalGroup
{
	Profiles,
	Automations,
	Scripts
}

public sealed class SecretApprovalPartition
{
	public Dictionary<Guid, Dictionary<string, SecretApprovalKey>> Owners { get; set; } = new();
}

public sealed class SecretApprovalKey
{
	public List<string> Approved { get; set; } = [];

	public List<string> Withheld { get; set; } = [];

	// Found stored without any record (an unreadable record file, an edit outside the app): never approved
	// until the user approves it explicitly.
	public List<string> Unknown { get; set; } = [];

	// Shown as the approved side of a diff only; it never approves anything.
	public string? Superseded { get; set; }
}

public sealed record SecretApprovalPartitionLoad(Guid PartitionId, SecretApprovalPartition? Partition, bool Unreadable);

public interface ISecretApprovalFiles
{
	bool IsBackfilled(SecretApprovalGroup group);

	void MarkBackfilled(SecretApprovalGroup group);

	IReadOnlyList<SecretApprovalPartitionLoad> LoadAll(SecretApprovalGroup group);

	void Save(SecretApprovalGroup group, Guid partitionId, SecretApprovalPartition partition);

	void Delete(SecretApprovalGroup group, Guid partitionId);
}
