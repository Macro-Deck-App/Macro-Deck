using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Mcp;

// Integration config entries, the Connect credential, the backup recovery key and any other preference
// hold secrets by id outside flows; a secret one of them names is never scrubbed for the MCP.
public sealed class McpSecretHolders : IMcpSecretHolders
{
	private readonly DatabaseContext _context;

	public McpSecretHolders(DatabaseContext context)
	{
		_context = context;
	}

	public async Task<IReadOnlySet<Guid>> HeldElsewhere(
		IReadOnlyCollection<Guid> candidates,
		CancellationToken cancellationToken)
	{
		var held = new HashSet<Guid>();
		if (candidates.Count == 0)
		{
			return held;
		}

		var entries = await _context.IntegrationConfigEntries
			.AsNoTracking()
			.Select(entry => entry.ValuesJson)
			.ToListAsync(cancellationToken);
		var preferences = await _context.AppPreferences
			.AsNoTracking()
			.Select(preference => preference.Value)
			.ToListAsync(cancellationToken);

		foreach (var candidate in candidates)
		{
			var text = candidate.ToString();
			if (entries.Concat(preferences)
				.Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase)))
			{
				held.Add(candidate);
			}
		}

		return held;
	}
}
