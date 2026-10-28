using System.ComponentModel;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets.Approvals;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp.Tools;

public sealed record McpApplyResult(
	bool Valid,
	bool Applied,
	string? BatchId,
	IReadOnlyList<McpProblem> Problems,
	IReadOnlyList<McpConfirmation> Confirmations,
	IReadOnlyDictionary<string, string> CreatedIds);

public sealed record McpUndoResult(bool Undone, string BatchId, IReadOnlyList<McpProblem> Problems);

[McpServerToolType]
public sealed class McpWriteTools
{
	public const int MaxChanges = 100;

	private readonly IMcpChangeSet _changes;
	private readonly IMcpJournal _journal;
	private readonly SecretWriteAuthority _authority;
	private readonly McpConfirmationText _confirmation;
	private readonly McpConfirmationGate _gate;

	public McpWriteTools(
		IMcpChangeSet changes,
		IMcpJournal journal,
		SecretWriteAuthority authority,
		McpConfirmationText confirmation,
		McpConfirmationGate gate)
	{
		_changes = changes;
		_journal = journal;
		_authority = authority;
		_confirmation = confirmation;
		_gate = gate;
	}

	[McpServerTool(Name = "apply_changes", Title = "Apply changes", Destructive = true, UseStructuredContent = true)]
	[Description("Creates, updates or deletes profiles, folders, widgets (with their action flows), automations, "
		+ "scripts and variables in one batch. Everything is validated first; with dryRun nothing is written. "
		+ "A create may carry a ref such as \"r1\"; a later change in the same batch names it as \"@r1\" in any id "
		+ "field. Applied batches can be reverted with undo. Blocks that use a secret stay inactive until the user "
		+ "approves them in Macro Deck.")]
	public async Task<McpApplyResult> ApplyChanges(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		[Description("The changes, applied in order. Each is { op: create|update|delete, entity: "
			+ "profile|folder|widget|automation|script|variable, id?, ref?, data? }.")]
		List<McpChange> changes,
		[Description("Validate only and report what would need the user's confirmation.")]
		bool dryRun = false,
		CancellationToken cancellationToken = default)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		var tokenId = McpConsent.TokenId(context);
		if (changes.Count is 0 or > MaxChanges)
		{
			throw McpToolErrors.Create(McpToolErrors.InvalidArgument, $"changes must hold 1 to {MaxChanges} entries.");
		}

		var validation = await _changes.Validate(tokenId, changes);
		if (dryRun || !validation.Valid)
		{
			return new McpApplyResult(validation.Valid,
				false,
				null,
				validation.Problems,
				validation.Confirmations,
				new Dictionary<string, string>());
		}

		var texts = new List<string>();
		foreach (var confirmation in validation.Confirmations)
		{
			texts.Add(await _confirmation.For(confirmation));
		}

		await _gate.Require(server,
			context,
			validation.Confirmations.Select(confirmation => $"{confirmation.Kind}: {confirmation.Detail}").ToList(),
			texts,
			cancellationToken);

		var result = await _changes.Apply(tokenId,
			changes,
			"apply_changes");
		return new McpApplyResult(true,
			result.Applied,
			result.BatchId?.ToString(),
			result.Problems,
			validation.Confirmations,
			result.CreatedIds);
	}

	[McpServerTool(Name = "undo", Title = "Undo", Destructive = true, UseStructuredContent = true)]
	[Description("Reverts a batch applied with apply_changes, by default the newest one this client has not undone. "
		+ "Objects changed since the batch are left as they are and reported.")]
	public async Task<McpUndoResult> Undo(
		RequestContext<CallToolRequestParams> context,
		[Description("Optional: the batchId apply_changes returned.")]
		string? batchId = null)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		var tokenId = McpConsent.TokenId(context);
		var batch = batchId is null
			? _journal.Batches().LastOrDefault(candidate => candidate.TokenId == tokenId && !candidate.Undone)
			: Guid.TryParse(batchId, out var id)
				? _journal.Find(id)
				: null;
		if (batch is null || batch.TokenId != tokenId || batch.Undone)
		{
			throw McpToolErrors.Create(McpToolErrors.NotFound, "There is no batch of this client to undo.");
		}

		var problems = await _changes.Undo(batch.BatchId);
		return new McpUndoResult(true, batch.BatchId.ToString(), problems);
	}
}
