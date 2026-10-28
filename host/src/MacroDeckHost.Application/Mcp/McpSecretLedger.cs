using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Secrets.Approvals;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpSecretLedger
{
	void Grant(Guid tokenId, Guid secretId);

	bool IsGranted(Guid tokenId, Guid secretId);

	void Defer(IEnumerable<Guid> secretIds);

	Task ForgetToken(Guid tokenId, CancellationToken cancellationToken = default);

	Task ScrubDeferred(CancellationToken cancellationToken = default);
}

public sealed class McpSecretLedger : IMcpSecretLedger
{
	private readonly IMcpStateStore _store;
	private readonly IFolderCache _folders;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly IMcpSecretHolders _holders;
	private readonly ISecretService _secrets;
	private readonly ISecretLineage? _lineage;
	private static readonly Lock _gate = new();

	public McpSecretLedger(
		IMcpStateStore store,
		IFolderCache folders,
		IAutomationCache automations,
		IScriptCache scripts,
		IMcpSecretHolders holders,
		ISecretService secrets,
		ISecretLineage? lineage = null)
	{
		_lineage = lineage;
		_store = store;
		_folders = folders;
		_automations = automations;
		_scripts = scripts;
		_holders = holders;
		_secrets = secrets;
	}

	public void Grant(Guid tokenId, Guid secretId)
	{
		lock (_gate)
		{
			var grants = Grants();
			if (!grants.TryGetValue(tokenId, out var ids))
			{
				ids = grants[tokenId] = [];
			}

			ids.Add(secretId);
			_store.Write(IMcpStateStore.GrantsDocument, grants);
		}
	}

	public bool IsGranted(Guid tokenId, Guid secretId)
	{
		lock (_gate)
		{
			// A widget owns clones of the secrets it was created with; a clone of a granted secret counts.
			return Grants().TryGetValue(tokenId, out var ids)
				&& (ids.Contains(secretId) || _lineage is not null && ids.Contains(_lineage.Root(secretId)));
		}
	}

	public void Defer(IEnumerable<Guid> secretIds)
	{
		lock (_gate)
		{
			var deferred = Deferred();
			deferred.UnionWith(secretIds);
			_store.Write(IMcpStateStore.DeferredScrubDocument, deferred);
		}
	}

	public async Task ForgetToken(Guid tokenId, CancellationToken cancellationToken = default)
	{
		HashSet<Guid> granted;
		lock (_gate)
		{
			var grants = Grants();
			if (!grants.Remove(tokenId, out var ids))
			{
				return;
			}

			granted = ids;
			_store.Write(IMcpStateStore.GrantsDocument, grants);
		}

		Defer(granted);
		await ScrubDeferred(cancellationToken);
	}

	public async Task ScrubDeferred(CancellationToken cancellationToken = default)
	{
		HashSet<Guid> candidates;
		lock (_gate)
		{
			candidates = Deferred();
		}

		if (candidates.Count == 0)
		{
			return;
		}

		var referenced = ReferencedInFlowsAndJournal();
		var held = await _holders.HeldElsewhere(candidates, cancellationToken);
		var scrubbed = new List<Guid>();
		foreach (var secretId in candidates.Where(id => !referenced.Contains(id) && !held.Contains(id)))
		{
			await _secrets.Delete(secretId);
			scrubbed.Add(secretId);
		}

		lock (_gate)
		{
			var deferred = Deferred();
			deferred.ExceptWith(scrubbed);
			deferred.ExceptWith(candidates.Where(id => !referenced.Contains(id) && held.Contains(id)));
			_store.Write(IMcpStateStore.DeferredScrubDocument, deferred);
		}
	}

	private HashSet<Guid> ReferencedInFlowsAndJournal()
	{
		var texts = _folders.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.Select(widget => widget.Data)
			.Concat(_automations.GetAll().Select(automation => automation.Flows))
			.Concat(_scripts.GetAll().Select(script => script.Flows));

		var referenced = _store.Read<McpJournalDocument>(IMcpStateStore.JournalDocument)?.SecretIds() ?? [];
		foreach (var text in texts)
		{
			referenced.UnionWith(SecretReferences.Extract(text));
		}

		return referenced;
	}

	private Dictionary<Guid, HashSet<Guid>> Grants()
	{
		return _store.Read<Dictionary<Guid, HashSet<Guid>>>(IMcpStateStore.GrantsDocument) ?? [];
	}

	private HashSet<Guid> Deferred()
	{
		return _store.Read<HashSet<Guid>>(IMcpStateStore.DeferredScrubDocument) ?? [];
	}
}
