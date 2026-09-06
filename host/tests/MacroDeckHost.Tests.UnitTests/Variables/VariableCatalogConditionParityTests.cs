using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// An unavailable variable must be exactly as unresolvable in a condition as a name nobody ever declared -
/// see <see cref="VariableTemplateRenderer.BuildContext"/>'s remarks. This pins the rule across both
/// variable kinds a "stale" reading can come from: a dynamic binding whose provider stopped pushing, and a
/// classic polled integration variable that fell behind its freshness window. The parity now spans the
/// state operators too: "unresolvable" is exactly what <c>isNotAvailable</c> reports, for both kinds of
/// staleness, and a third arrangement below pins that a declared, available, empty-valued variable is
/// still distinguishable from an unresolvable one.
/// </summary>
[TestFixture]
internal sealed class VariableCatalogConditionParityTests
{
	private const string IntegrationId = "com.example.smart-home";
	private const string BrightnessId = "entity/light.living_room/brightness";

	// The right-hand side of every case below is the numeric literal 128 (see Evaluate). An unresolvable
	// left side must resolve to null (ActionConditionEvaluator.ResolveSide's Object case, Null/Undefined
	// case) and Stringify(null) is "" (ActionConditionEvaluator.Stringify), so these are ordinary
	// string-comparison outcomes of "" against "128", spelled out as literals rather than recomputed from
	// the production algorithm - the whole point of this test is to catch that algorithm being wrong.
	// ">" in particular used to come out true: ResolveSide's Object case fell back to the reference's own
	// raw JSON text (e.g. {"$var":"..."}, which starts with '{') instead of null when a $var/$event
	// reference failed to resolve, so "brightness > 128" evaluated true while brightness was unavailable -
	// exactly backwards for a rule whose purpose is to stop conditions from acting on data the host does
	// not have.
	[TestCase("==", ExpectedResult = false)]
	[TestCase("!=", ExpectedResult = true)]
	[TestCase(">", ExpectedResult = false)]
	[TestCase("contains", ExpectedResult = false)]
	[TestCase("isNotAvailable", ExpectedResult = true)]
	[TestCase("isAvailable", ExpectedResult = false)]
	[TestCase("isEmpty", ExpectedResult = false)]
	[TestCase("isNotEmpty", ExpectedResult = false)]
	public async Task<bool> An_unavailable_dynamic_variable_is_unresolvable_exactly_like_an_unknown_name(string op)
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(new VariableDefinition
		{
			Id = BrightnessId,
			Name = "brightness",
			Type = SdkVariableType.Numeric,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
		});
		provider.SetValue(BrightnessId, 128m);
		var harness = new VariableCatalogHarness(provider);

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.True);

		// The resource goes away: unavailable, but the stale value (128) is still sitting in the entity.
		harness.Channel.Write(IntegrationId, BrightnessId, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.False);

		var boundName = bound.Data!.Name;
		var unavailableVerdict = Evaluate(harness.Registry, boundName, op);

		// Separate from the literal assertion above (via ExpectedResult): this is what actually pins
		// "exactly like an unknown name" - a name nobody ever declared goes through the identical
		// unresolvable path, so the two must still agree even if the literal expectations above turn out
		// to encode a shared mistake.
		var unknownVerdict = Evaluate(harness.Registry, "no_such_variable_at_all", op);
		Assert.That(unavailableVerdict,
			Is.EqualTo(unknownVerdict),
			$"an unavailable dynamic variable must give the same '{op}' verdict as a name that never existed");

		return unavailableVerdict;
	}

	// Same shape as the dynamic-variable case above: an unresolvable "$var" reference resolves to null,
	// so this is "" compared against "128" per operator.
	[TestCase("==", ExpectedResult = false)]
	[TestCase("!=", ExpectedResult = true)]
	[TestCase(">", ExpectedResult = false)]
	[TestCase("contains", ExpectedResult = false)]
	[TestCase("isNotAvailable", ExpectedResult = true)]
	[TestCase("isAvailable", ExpectedResult = false)]
	[TestCase("isEmpty", ExpectedResult = false)]
	[TestCase("isNotEmpty", ExpectedResult = false)]
	public bool A_classic_polled_variable_past_its_freshness_window_is_unresolvable_the_same_way(string op)
	{
		var registry = new VariableRegistry();
		var entity = new VariableEntity
		{
			Id = Guid.CreateVersion7(),
			Name = "stale_classic",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = IntegrationId,
			DefinitionId = "declared-var",
			Value = "2024.6.1",
			UpdateMode = VariableUpdateMode.Polled,
			UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5),
			CreatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5)
		};
		registry.Upsert(entity);
		Assert.That(registry.IsAvailable(entity.Id), Is.False);

		var staleVerdict = Evaluate(registry, "stale_classic", op);

		// Separate from the literal assertion above (via ExpectedResult) for the same reason as the
		// dynamic-variable case: this pins "the same way as an unknown name" independently of whether the
		// literal expectations above are themselves correct.
		var unknownVerdict = Evaluate(registry, "no_such_variable_at_all", op);
		Assert.That(staleVerdict,
			Is.EqualTo(unknownVerdict),
			$"a stale classic integration variable must give the same '{op}' verdict as a name that never existed");

		return staleVerdict;
	}

	// Without this arrangement, an implementation that answered isEmpty for every unresolvable-or-empty
	// operand alike would still pass both parity cases above - they never exercise a variable that is both
	// declared and available. This is the case that distinguishes "empty" from "unresolvable".
	[Test]
	public void A_declared_available_empty_valued_variable_is_empty_but_not_unresolvable()
	{
		var registry = new VariableRegistry();
		var entity = new VariableEntity
		{
			Id = Guid.CreateVersion7(),
			Name = "empty_but_available",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = string.Empty
		};
		registry.Upsert(entity);
		Assert.That(registry.IsAvailable(entity.Id), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(Evaluate(registry, "empty_but_available", "isEmpty"), Is.True);
			Assert.That(Evaluate(registry, "empty_but_available", "isNotAvailable"), Is.False);
		});
	}

	private static bool Evaluate(VariableRegistry registry, string variableName, string op)
	{
		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).Result;

		var left = JsonDocument.Parse($$"""{"$var":"{{variableName}}"}""").RootElement;
		var right = JsonDocument.Parse("""128""").RootElement;

		return evaluator.Evaluate(left, op, right, context);
	}
}
