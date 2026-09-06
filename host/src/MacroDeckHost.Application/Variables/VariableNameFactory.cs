using System.Globalization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableNameFactory
{
	public const int MaxNameLength = 64;

	private const int HashTailLength = 9;

	private readonly VariableRegistry _registry;

	public VariableNameFactory(VariableRegistry registry)
	{
		_registry = registry;
	}

	// Picks the canonical name a binding will occupy. The result is written into the binding and never
	// recomputed: derivation depends on what else happens to be registered, so re-deriving on a later
	// load would let two bindings swap names purely because they loaded in a different order.
	//
	// "taken" is names already claimed in this pass but not yet in the registry, so a batch of bindings
	// materialized together does not hand the same name to two of them.
	public string Derive(
		string integrationId,
		string localResourceId,
		string? suggestedName,
		IReadOnlySet<string>? taken = null)
	{
		var candidate = VariableNameSanitizer.Sanitize(suggestedName);
		if (!VariableNameSanitizer.IsValid(candidate))
		{
			candidate = VariableNameSanitizer.Sanitize(localResourceId);
		}

		if (!VariableNameSanitizer.IsValid(candidate))
		{
			candidate = "v_" + Hash(integrationId, localResourceId);
		}

		candidate = Shorten(candidate, integrationId, localResourceId, MaxNameLength);

		if (IsFree(candidate, integrationId, localResourceId, taken))
		{
			return candidate;
		}

		for (var suffix = 2;; suffix++)
		{
			var tail = "_" + suffix.ToString(CultureInfo.InvariantCulture);
			var next = Shorten(candidate, integrationId, localResourceId, MaxNameLength - tail.Length) + tail;
			if (IsFree(next, integrationId, localResourceId, taken))
			{
				return next;
			}
		}
	}

	// A name held by this very resource is a reuse, not a collision - that is the path a rehydrated
	// binding and a reconnecting provider both take.
	private bool IsFree(
		string name,
		string integrationId,
		string localResourceId,
		IReadOnlySet<string>? taken)
	{
		if (taken is not null && taken.Contains(name))
		{
			return false;
		}

		var existing = _registry.FindByName(VariableScope.Global, null, name);
		return existing is null ||
			(string.Equals(existing.OwnerIntegrationId, integrationId, StringComparison.Ordinal) &&
				string.Equals(existing.DefinitionId, localResourceId, StringComparison.Ordinal));
	}

	private static string Shorten(string candidate, string integrationId, string localResourceId, int maxLength)
	{
		if (candidate.Length <= maxLength)
		{
			return candidate;
		}

		var head = candidate[..Math.Max(maxLength - HashTailLength, 1)].TrimEnd('_');
		return (head.Length == 0 ? "v" : head) + "_" + Hash(integrationId, localResourceId);
	}

	// Over the qualified id rather than the display name, so the tail stays stable when a resource is
	// renamed upstream and stays distinct between two resources that read alike.
	private static string Hash(string integrationId, string localResourceId)
	{
		var hash = 2166136261u;
		foreach (var character in integrationId)
		{
			hash = (hash ^ character) * 16777619u;
		}

		hash = (hash ^ ':') * 16777619u;

		foreach (var character in localResourceId)
		{
			hash = (hash ^ character) * 16777619u;
		}

		return hash.ToString("x8", CultureInfo.InvariantCulture);
	}
}
