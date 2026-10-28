using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeckHost.Application.Secrets.Approvals;

public interface ISecretApprovals
{
	bool IsApproved(FlowOwner owner, string blockId, FlowBlockContent block);

	bool IsDraftApproved(FlowOwner? owner, string blockId, FlowBlockContent block);

	string? ApprovedSnapshot(FlowOwner owner, string blockId);

	bool IsUnknown(FlowOwner owner, string blockId, FlowBlockContent block);

	string Fingerprint(FlowBlockContent block);

	bool Approve(FlowOwner owner, FlowBlockSnapshot current, string fingerprint);
}

public sealed class SecretApprovalLedger : ISecretApprovals
{
	private const int RetiredSnapshotLimit = 8;

	private readonly Func<string, string, IActionDefinition?> _findAction;
	private readonly ISecretLineage? _lineage;
	private readonly ISecretApprovalFiles? _files;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<FlowOwner, Dictionary<string, SecretApprovalKey>> _owners = new();
	private readonly Dictionary<FlowOwner, (SecretApprovalGroup Group, Guid PartitionId)> _partitions = new();

	public SecretApprovalLedger(
		Func<string, string, IActionDefinition?> findAction,
		ISecretLineage? lineage,
		ISecretApprovalFiles? files,
		ILogger logger)
	{
		_findAction = findAction;
		_lineage = lineage;
		_files = files;
		_logger = logger.ForContext<SecretApprovalLedger>();
	}

	public void LoadGroup(
		SecretApprovalGroup group,
		IReadOnlyDictionary<Guid, IReadOnlyList<(FlowOwner Owner, string? Flows)>> partitions)
	{
		if (_files is null)
		{
			return;
		}

		lock (_gate)
		{
			var backfilled = _files.IsBackfilled(group);
			var loaded = _files.LoadAll(group).ToDictionary(load => load.PartitionId);

			foreach (var (partitionId, owners) in partitions)
			{
				loaded.TryGetValue(partitionId, out var load);
				if (load?.Unreadable == true)
				{
					_logger.Error(
						"Secret approvals of {Group} partition {PartitionId} are unreadable; nothing in it is approved",
						group,
						partitionId);
				}

				foreach (var (owner, flows) in owners)
				{
					_partitions[owner] = (group, partitionId);
					if (load?.Partition?.Owners.TryGetValue(owner.Id, out var keys) == true)
					{
						_owners[owner] = keys;
					}
					else if (!backfilled)
					{
						_owners[owner] = Backfill(flows);
					}

					MarkUnknown(owner, FlowSecretBlocks.FromFlows(flows));
				}
			}

			if (!backfilled)
			{
				foreach (var partitionId in partitions.Keys)
				{
					SavePartitionLocked(group, partitionId);
				}

				_files.MarkBackfilled(group);
				_logger.Information("Approved the existing secret-carrying flow blocks of {Group}", group);
			}
		}
	}

	public void Record(FlowOwner owner, IReadOnlyList<FlowBlockSnapshot> blocks, SecretApprovalWrite write)
	{
		lock (_gate)
		{
			if (blocks.Count == 0 && !_owners.ContainsKey(owner))
			{
				return;
			}

			var keys = KeysFor(owner);
			var lineageIndex = new Lazy<(HashSet<string> Approved, HashSet<string> Withheld)>(BuildIndex);

			foreach (var block in blocks.Where(block => !string.IsNullOrEmpty(block.Id)))
			{
				var normalized = Normalize(block.Content);
				keys.TryGetValue(block.Id, out var key);
				if (key is not null && IsRecorded(key, normalized))
				{
					continue;
				}

				key ??= keys[block.Id] = new SecretApprovalKey();
				var approvedElsewhere = lineageIndex.Value.Approved.Contains(normalized);
				var isNewKey = key.Approved.Count == 0
					&& key.Withheld.Count == 0
					&& key.Unknown.Count == 0
					&& key.Superseded is null;

				if (block.Content.HasDuplicateParameterNames)
				{
					key.Withheld.Add(block.Raw);
				}
				else if (isNewKey && approvedElsewhere)
				{
					key.Approved.Add(block.Raw);
				}
				else if (!approvedElsewhere && lineageIndex.Value.Withheld.Contains(normalized))
				{
					key.Withheld.Add(block.Raw);
				}
				else if (write.Mode == SecretApprovalMode.Approve
					|| write.Mode == SecretApprovalMode.Rewrite && RewritesApproved(key, write.Rewriter, normalized)
					|| write.Mode == SecretApprovalMode.Restore && RestoresApproved(write.Restored, normalized))
				{
					key.Approved.Add(block.Raw);
				}
				else
				{
					key.Withheld.Add(block.Raw);
				}
			}

			Prune(keys, blocks);
		}
	}

	public void Assign(FlowOwner owner, SecretApprovalGroup group, Guid partitionId)
	{
		lock (_gate)
		{
			_partitions[owner] = (group, partitionId);
		}
	}

	public void SavePartition(SecretApprovalGroup group, Guid partitionId)
	{
		lock (_gate)
		{
			SavePartitionLocked(group, partitionId);
		}
	}

	public void DeletePartition(SecretApprovalGroup group, Guid partitionId)
	{
		lock (_gate)
		{
			foreach (var owner in OwnersOf(group, partitionId))
			{
				_owners.Remove(owner);
				_partitions.Remove(owner);
			}

			_files?.Delete(group, partitionId);
		}
	}

	public bool IsApproved(FlowOwner owner, string blockId, FlowBlockContent block)
	{
		if (string.IsNullOrEmpty(blockId) || block.HasDuplicateParameterNames)
		{
			return false;
		}

		lock (_gate)
		{
			return _owners.TryGetValue(owner, out var keys)
				&& keys.TryGetValue(blockId, out var key)
				&& Matches(key.Approved, Normalize(block));
		}
	}

	public bool IsDraftApproved(FlowOwner? owner, string blockId, FlowBlockContent block)
	{
		if (string.IsNullOrEmpty(blockId) || block.HasDuplicateParameterNames)
		{
			return false;
		}

		lock (_gate)
		{
			var normalized = Normalize(block);
			if (owner is { } known
				&& _owners.TryGetValue(known, out var keys)
				&& keys.TryGetValue(blockId, out var key))
			{
				if (Matches(key.Approved, normalized))
				{
					return true;
				}

				if (Matches(key.Withheld, normalized) || Matches(key.Unknown, normalized))
				{
					return false;
				}
			}

			var (approved, withheld) = BuildIndex();
			return approved.Contains(normalized) || !withheld.Contains(normalized);
		}
	}

	public bool IsUnknown(FlowOwner owner, string blockId, FlowBlockContent block)
	{
		lock (_gate)
		{
			return _owners.TryGetValue(owner, out var keys)
				&& keys.TryGetValue(blockId, out var key)
				&& Matches(key.Unknown, Normalize(block));
		}
	}

	public string? ApprovedSnapshot(FlowOwner owner, string blockId)
	{
		lock (_gate)
		{
			return _owners.TryGetValue(owner, out var keys) && keys.TryGetValue(blockId, out var key)
				? key.Approved.LastOrDefault() ?? key.Superseded
				: null;
		}
	}

	public int RetainedSnapshotCount(FlowOwner owner)
	{
		lock (_gate)
		{
			return _owners.TryGetValue(owner, out var keys)
				? keys.Values.Sum(key => key.Approved.Count + key.Withheld.Count + key.Unknown.Count)
				: 0;
		}
	}

	public string Fingerprint(FlowBlockContent block)
	{
		return FlowBlockNormalizer.Fingerprint(Normalize(block));
	}

	public bool Approve(FlowOwner owner, FlowBlockSnapshot current, string fingerprint)
	{
		lock (_gate)
		{
			var normalized = Normalize(current.Content);
			if (string.IsNullOrEmpty(current.Id)
				|| current.Content.HasDuplicateParameterNames
				|| !string.Equals(FlowBlockNormalizer.Fingerprint(normalized), fingerprint, StringComparison.Ordinal))
			{
				return false;
			}

			var keys = KeysFor(owner);
			if (!keys.TryGetValue(current.Id, out var key))
			{
				key = keys[current.Id] = new SecretApprovalKey();
			}

			key.Approved.Add(current.Raw);
			var changed = new HashSet<FlowOwner> { owner };
			foreach (var (other, otherKeys) in _owners)
			{
				foreach (var otherKey in otherKeys.Values)
				{
					var removed = otherKey.Withheld.RemoveAll(snapshot => SnapshotNormalized(snapshot) == normalized)
						+ otherKey.Unknown.RemoveAll(snapshot => SnapshotNormalized(snapshot) == normalized);
					if (removed > 0)
					{
						changed.Add(other);
					}
				}
			}

			foreach (var (group, partitionId) in changed
				.Where(_partitions.ContainsKey)
				.Select(changedOwner => _partitions[changedOwner])
				.Distinct()
				.ToList())
			{
				SavePartitionLocked(group, partitionId);
			}

			return true;
		}
	}

	private static Dictionary<string, SecretApprovalKey> Backfill(string? flows)
	{
		var keys = new Dictionary<string, SecretApprovalKey>(StringComparer.Ordinal);
		foreach (var block in FlowSecretBlocks.FromFlows(flows))
		{
			if (string.IsNullOrEmpty(block.Id))
			{
				continue;
			}

			if (!keys.TryGetValue(block.Id, out var key))
			{
				key = keys[block.Id] = new SecretApprovalKey();
			}

			key.Approved.Add(block.Raw);
		}

		return keys;
	}

	private void MarkUnknown(FlowOwner owner, IReadOnlyList<FlowBlockSnapshot> blocks)
	{
		foreach (var block in blocks.Where(block => !string.IsNullOrEmpty(block.Id)))
		{
			var keys = KeysFor(owner);
			if (!keys.TryGetValue(block.Id, out var key))
			{
				key = keys[block.Id] = new SecretApprovalKey();
			}

			if (!IsRecorded(key, Normalize(block.Content)))
			{
				key.Unknown.Add(block.Raw);
			}
		}
	}

	// A snapshot whose action is not in the catalog is compared without it: the editor leaves such a block
	// untouched, so an unchanged block still matches, and any edit made while the plugin is away counts.
	private void Prune(Dictionary<string, SecretApprovalKey> keys, IReadOnlyList<FlowBlockSnapshot> blocks)
	{
		var current = blocks
			.GroupBy(block => block.Id, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => group.Select(block => block.Content).ToList(),
				StringComparer.Ordinal);

		foreach (var blockId in keys.Keys.ToList())
		{
			var key = keys[blockId];
			var stored = current.GetValueOrDefault(blockId) ?? [];
			var superseded = key.Approved.Where(snapshot => !MatchesAny(snapshot, stored)).ToList();
			if (superseded.Count > 0)
			{
				key.Superseded = superseded[^1];
				key.Approved.RemoveAll(superseded.Contains);
			}

			Retire(key.Withheld, stored);
			Retire(key.Unknown, stored);
			if (key is { Approved.Count: 0, Withheld.Count: 0, Unknown.Count: 0, Superseded: null })
			{
				keys.Remove(blockId);
			}
		}
	}

	// Snapshots no stored block uses any more are kept only as a bounded history, so a cut block pasted
	// elsewhere is still recognised without the record growing with every untrusted write.
	private void Retire(List<string> snapshots, List<FlowBlockContent> stored)
	{
		var retired = snapshots.Where(snapshot => !MatchesAny(snapshot, stored)).ToList();
		foreach (var snapshot in retired.Take(Math.Max(0, retired.Count - RetiredSnapshotLimit)))
		{
			snapshots.Remove(snapshot);
		}
	}

	private bool MatchesAny(string snapshot, List<FlowBlockContent> stored)
	{
		var parsed = FlowSecretBlocks.Parse(snapshot);
		if (parsed is null)
		{
			return false;
		}

		var known = IsKnown(parsed.Content);
		var normalized = known
			? Normalize(parsed.Content)
			: FlowBlockNormalizer.Normalize(parsed.Content, null, _lineage);
		return stored.Any(block => (known
				? Normalize(block)
				: FlowBlockNormalizer.Normalize(block, null, _lineage))
			== normalized);
	}

	private bool IsRecorded(SecretApprovalKey key, string normalized)
	{
		return Matches(key.Approved, normalized)
			|| Matches(key.Withheld, normalized)
			|| Matches(key.Unknown, normalized);
	}

	private bool RewritesApproved(SecretApprovalKey key, Func<string, string>? rewriter, string normalized)
	{
		return rewriter is not null
			&& key.Approved.Any(snapshot => SnapshotNormalized(rewriter(snapshot)) == normalized);
	}

	private bool RestoresApproved(IReadOnlyCollection<string>? restored, string normalized)
	{
		return restored is not null && restored.Any(snapshot => SnapshotNormalized(snapshot) == normalized);
	}

	private (HashSet<string> Approved, HashSet<string> Withheld) BuildIndex()
	{
		var approved = new HashSet<string>(StringComparer.Ordinal);
		var withheld = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in _owners.Values.SelectMany(keys => keys.Values))
		{
			foreach (var snapshot in key.Approved)
			{
				approved.Add(SnapshotNormalized(snapshot));
			}

			foreach (var snapshot in key.Withheld.Concat(key.Unknown))
			{
				withheld.Add(SnapshotNormalized(snapshot));
			}
		}

		return (approved, withheld);
	}

	private bool Matches(List<string> snapshots, string normalized)
	{
		return snapshots.Any(snapshot => SnapshotNormalized(snapshot) == normalized);
	}

	private bool IsKnown(FlowBlockContent block)
	{
		return block is { IntegrationId: { } integrationId, ActionId: { } actionId }
			&& _findAction(integrationId, actionId) is not null;
	}

	private string SnapshotNormalized(string snapshot)
	{
		var parsed = FlowSecretBlocks.Parse(snapshot);
		return parsed is null ? string.Empty : Normalize(parsed.Content);
	}

	private string Normalize(FlowBlockContent block)
	{
		var action = block is { IntegrationId: { } integrationId, ActionId: { } actionId }
			? _findAction(integrationId, actionId)
			: null;
		return FlowBlockNormalizer.Normalize(block, action, _lineage);
	}

	private Dictionary<string, SecretApprovalKey> KeysFor(FlowOwner owner)
	{
		if (!_owners.TryGetValue(owner, out var keys))
		{
			keys = _owners[owner] = new Dictionary<string, SecretApprovalKey>(StringComparer.Ordinal);
		}

		return keys;
	}

	private List<FlowOwner> OwnersOf(SecretApprovalGroup group, Guid partitionId)
	{
		return _partitions
			.Where(entry => entry.Value.Group == group && entry.Value.PartitionId == partitionId)
			.Select(entry => entry.Key)
			.ToList();
	}

	private void SavePartitionLocked(SecretApprovalGroup group, Guid partitionId)
	{
		if (_files is null)
		{
			return;
		}

		var partition = new SecretApprovalPartition();
		foreach (var owner in OwnersOf(group, partitionId))
		{
			if (_owners.TryGetValue(owner, out var keys) && keys.Count > 0)
			{
				partition.Owners[owner.Id] = keys;
			}
		}

		_files.Save(group, partitionId, partition);
	}
}
