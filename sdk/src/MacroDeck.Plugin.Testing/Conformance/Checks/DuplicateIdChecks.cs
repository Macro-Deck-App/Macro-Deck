using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC04xx - B4: duplicate ids. A declared (kind, localId) collision cannot reach a running subject at all
// (PluginHostBuilder.Build rejects it), so MDC0401 is a regression guard read from the wire. What earns
// this category its runtime keep: two provider instances (MDC0402) and two provided variables (MDC0403)
// both choose their colliding identity at runtime, after the plugin has already started, where no
// build-time check can see it - see the misbehaving fixture's duplicate-instance-ids and
// duplicate-variable-definition-ids flags. The negative case B4 also calls out - the same local id under
// two different kinds is legal and must never be flagged - is deliberately not its own check id here: it
// is proven by construction, since the misbehaving fixture's own well-behaved variable and its "stall"
// action share that exact local id, and MDC0401 running clean against that fixture in this suite's own
// "well-behaved subject, zero Required failures" test is what demonstrates a correct implementation never
// reports it.

/// <summary>Regression guard: no two declared capabilities, read from the wire, share a (kind, localId) pair.</summary>
internal sealed class NoDuplicateDeclaredIdsCheck() : ConformanceCheckBase("MDC0401",
	"No two declared capabilities share the same (kind, localId) pair",
	ConformanceCategory.DuplicateIds,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var seen = new HashSet<(string Kind, string LocalId)>();

		foreach (var capability in context.Session!.Declared)
		{
			if (!seen.Add((capability.Kind, capability.LocalId)))
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					"No two declared capabilities share the same (kind, localId) pair.",
					$"'{capability.Kind}'/'{capability.LocalId}' is declared more than once."));
			}
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("declared capabilities", context.Session.Declared.Count)
		]));
	}
}

/// <summary>Real violator: <c>MisbehaviorFlags.DuplicateInstanceIds</c> - two weather station instances both returning "primary".</summary>
internal sealed class NoDuplicateProviderInstanceIdsCheck() : ConformanceCheckBase("MDC0402",
	"No two weather station instances share an instance id",
	ConformanceCategory.DuplicateIds,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Session!.Declared.All(capability =>
			!string.Equals(capability.Kind, CapabilityKinds.Weather, StringComparison.Ordinal)))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the weather capability.");
		}

		var outcome = await context.Session.Weather.GetInstancesAsync().ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"weather/instances succeeds for a subject that declared the weather capability.",
				$"weather/instances failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var instances = outcome.DataAs<WeatherInstancesResult>()?.Instances ?? [];
		var duplicates = instances.GroupBy(instance => instance.Id, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToList();

		return duplicates.Count == 0
			? ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("weather instances checked", instances.Count)
			])
			: ConformanceCheckResult.Fail("No two weather station instances share an instance id.",
				$"Instance id(s) declared more than once: {string.Join(", ", duplicates)}.");
	}
}

/// <summary>
/// Real violator: <c>MisbehaviorFlags.DuplicateVariableDefinitionIds</c> - two eager variables resolving
/// to the same id. Scoped to the eager half: a catalog resource id is never declared as a capability and
/// is not enumerable from here at all, so uniqueness across a catalog is not a claim a describe payload
/// can support.
/// </summary>
internal sealed class NoDuplicateVariableDefinitionIdsCheck() : ConformanceCheckBase("MDC0403",
	"No two eager variables resolve to the same id",
	ConformanceCategory.DuplicateIds,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Session!.Declared.All(capability =>
			!string.Equals(capability.Kind, CapabilityKinds.Variables, StringComparison.Ordinal)))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the variables capability.");
		}

		var outcome = await context.Session.Variables.DescribeAsync().ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"variables/describe succeeds for a subject that declared the variables capability.",
				$"variables/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var catalog = outcome.DataAs<VariableCatalogPayload>();

		if (catalog is null)
		{
			return ConformanceCheckResult.Inconclusive(
				"variables/describe's result could not be read as a VariableCatalogPayload.");
		}

		// A null Id means the provider never set one; the effective identity a host resolves it to is
		// derived from Name instead - see VariableDefinitionDto.Id's own remarks.
		var eager = catalog.Variables.Where(VariableCatalogProbe.IsEager).ToList();

		var duplicates = eager
			.Select(variable => VariableCatalogProbe.LocalIdOf(variable) ?? variable.Name)
			.Where(id => id is not null)
			.GroupBy(id => id, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key!)
			.ToList();

		return duplicates.Count == 0
			? ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("eager variables checked", eager.Count)
			])
			: ConformanceCheckResult.Fail(
				"No two eager variables resolve to the same id (or, when neither declares one, to the same effective identity derived from Name).",
				$"Shared identity: {string.Join(", ", duplicates)}.");
	}
}
