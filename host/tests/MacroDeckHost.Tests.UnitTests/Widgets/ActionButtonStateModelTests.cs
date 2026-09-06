using System.Text.Json.Nodes;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

/// <summary>
/// The legacy-upgrade matrix (scenarios A1-A6): a boolean toggle with an off/on face pair reads as the
/// current N-state shape entirely in memory, and a second read/save is idempotent - no resurrected
/// keys, no duplicated mapping rule.
/// </summary>
[TestFixture]
public class ActionButtonStateModelTests
{
	[Test]
	public void LegacyToggleButton_UpgradesToTwoStatesWithLiteralOffAndOnIds()
	{
		const string stored =
			"""
			{"mode":"toggle","isToggled":true,
			 "states":{"off":{"label":"Muted","backgroundColor":"#111111"},"on":{"label":"Live","backgroundColor":"#222222"}}}
			""";

		var model = ActionButtonStateModel.Read(stored);

		Assert.Multiple(() =>
		{
			Assert.That(model.StateMode, Is.True);
			Assert.That(model.States, Has.Count.EqualTo(2));
			Assert.That(model.States[0].Id, Is.EqualTo("off"), "generated GUID ids must fail this");
			Assert.That(model.States[0].Label, Is.EqualTo("Off"));
			Assert.That(model.States[0].Appearance!["label"]!.GetValue<string>(), Is.EqualTo("Muted"));
			Assert.That(model.States[0].Appearance!["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#111111"));
			Assert.That(model.States[1].Id, Is.EqualTo("on"), "generated GUID ids must fail this");
			Assert.That(model.States[1].Label, Is.EqualTo("On"));
			Assert.That(model.States[1].Appearance!["label"]!.GetValue<string>(), Is.EqualTo("Live"));
			Assert.That(model.States[1].Appearance!["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#222222"));
			Assert.That(model.ActiveStateId, Is.EqualTo("on"));
		});
	}

	[Test]
	public void LegacyToggleWithDeprecatedAliases_UpgradesFromOffStateAndOnState()
	{
		const string aliasOnly =
			"""{"mode":"toggle","offState":{"label":"Off face"},"onState":{"label":"On face"}}""";

		var aliasModel = ActionButtonStateModel.Read(aliasOnly);

		Assert.Multiple(() =>
		{
			Assert.That(aliasModel.FindState("off")!.Appearance!["label"]!.GetValue<string>(), Is.EqualTo("Off face"));
			Assert.That(aliasModel.FindState("on")!.Appearance!["label"]!.GetValue<string>(), Is.EqualTo("On face"));
		});

		const string statesWins =
			"""
			{"mode":"toggle","states":{"off":{"label":"NEW off"},"on":{"label":"NEW on"}},
			 "offState":{"label":"OLD off"},"onState":{"label":"OLD on"}}
			""";

		var winsModel = ActionButtonStateModel.Read(statesWins);

		Assert.Multiple(() =>
		{
			Assert.That(winsModel.FindState("off")!.Appearance!["label"]!.GetValue<string>(), Is.EqualTo("NEW off"));
			Assert.That(winsModel.FindState("on")!.Appearance!["label"]!.GetValue<string>(), Is.EqualTo("NEW on"));
		});
	}

	[Test]
	public void LegacyStateBinding_BecomesOneRuleMappingSelectingOnWithOffFallback()
	{
		const string stored =
			"""
			{"mode":"toggle","stateBinding":{"kind":"compare","id":"c","left":{"$var":"cpu"},"operator":">=","right":80}}
			""";

		var model = ActionButtonStateModel.Read(stored);

		Assert.Multiple(() =>
		{
			Assert.That(model.StateProvider, Is.Null);
			Assert.That(model.StateMapping, Is.Not.Null);
			Assert.That(model.StateMapping!.Rules, Has.Count.EqualTo(1));
			Assert.That(model.StateMapping.Rules[0].StateId, Is.EqualTo("on"));
			Assert.That(model.StateMapping.FallbackStateId, Is.EqualTo("off"));

			var storedWhen
				= JsonNode.Parse("""{"kind":"compare","id":"c","left":{"$var":"cpu"},"operator":">=","right":80}""")!;
			var ruleWhen = JsonNode.Parse(model.StateMapping.Rules[0].When.GetRawText())!;
			Assert.That(JsonNode.DeepEquals(ruleWhen, storedWhen),
				Is.True,
				"when must deep-equal the stored expression");
		});

		// The upgraded mapping resolves identically to the old derived toggle.
		var registry = new VariableRegistry();
		var evaluator = new ActionConditionEvaluator(new VariableTemplateRenderer(registry));
		var rule = model.StateMapping!.Rules[0];

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.EvaluateExpression(rule.When, ContextWith(registry, "cpu", "85")), Is.True);
			Assert.That(evaluator.EvaluateExpression(rule.When, ContextWith(registry, "cpu", "50")), Is.False);
		});
	}

	[Test]
	public void LegacyMomentaryButton_UpgradesToStateModeDisabled()
	{
		const string stored = """{"mode":"momentary","label":"Go","iconId":"icon-1","backgroundColor":"#abcdef"}""";

		var model = ActionButtonStateModel.Read(stored);

		Assert.Multiple(() =>
		{
			Assert.That(model.StateMode, Is.False);
			Assert.That(model.States, Is.Empty);
			Assert.That(model.ActiveStateId, Is.Null);
			Assert.That(model.StateMapping, Is.Null);
			Assert.That(model.Data["label"]!.GetValue<string>(), Is.EqualTo("Go"));
			Assert.That(model.Data["iconId"]!.GetValue<string>(), Is.EqualTo("icon-1"));
			Assert.That(model.Data["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#abcdef"));
		});
	}

	[Test]
	public void UpgradedButton_DropsLegacyKeysAndIsIdempotentAcrossASecondRead()
	{
		const string stored =
			"""
			{"mode":"toggle","isToggled":true,"stateBinding":{"kind":"compare","id":"c","left":{"$var":"cpu"},"operator":">=","right":80},
			 "offState":{"label":"Off"},"onState":{"label":"On"}}
			""";

		var firstPass = ActionButtonStateJson.Normalize(ActionButtonStateJson.ParseDataBag(stored));

		Assert.Multiple(() =>
		{
			Assert.That(firstPass.ContainsKey("mode"), Is.False);
			Assert.That(firstPass.ContainsKey("isToggled"), Is.False);
			Assert.That(firstPass.ContainsKey("stateBinding"), Is.False);
			Assert.That(firstPass.ContainsKey("offState"), Is.False);
			Assert.That(firstPass.ContainsKey("onState"), Is.False);
		});

		var firstModel = ActionButtonStateModel.Read((JsonObject)firstPass.DeepClone());
		var secondPass = ActionButtonStateJson.Normalize((JsonObject)firstPass.DeepClone());
		var secondModel = ActionButtonStateModel.Read((JsonObject)secondPass.DeepClone());

		Assert.Multiple(() =>
		{
			Assert.That(secondModel.StateMapping!.Rules, Has.Count.EqualTo(1), "no second mapping rule appears");
			Assert.That(JsonNode.DeepEquals(firstModel.Data, secondModel.Data),
				Is.True,
				"a second read/save must be a no-op");
		});
	}

	[Test]
	public void UnknownKeysInStoredData_SurviveTheUpgrade()
	{
		// Constructed from the existing object (never a fresh one): the schema's tolerate-unknown-keys
		// contract is explicit - Normalize must mutate and return the same bag, not rebuild it.
		var data = (JsonObject)JsonNode.Parse(
			"""{"mode":"toggle","isToggled":true,"someFuturePluginKey":{"nested":42}}""")!;

		var normalized = ActionButtonStateJson.Normalize(data);

		Assert.Multiple(() =>
		{
			Assert.That(ReferenceEquals(normalized, data), Is.True, "the same object must be mutated and returned");
			Assert.That(normalized["someFuturePluginKey"], Is.Not.Null);
			Assert.That(normalized["someFuturePluginKey"]!["nested"]!.GetValue<int>(), Is.EqualTo(42));
		});
	}

	private static VariableContext ContextWith(VariableRegistry registry, string name, string value)
	{
		registry.Upsert(new Domain.Entities.VariableEntity
		{
			Name = name,
			Scope = Domain.Enums.VariableScope.Global,
			Type = Domain.Enums.VariableType.Numeric,
			Classification = Domain.Enums.VariableClassification.User,
			Value = value
		});

		return new VariableTemplateRenderer(registry).CreateContextAsync(Domain.Enums.VariableScope.Global, null)
			.GetAwaiter()
			.GetResult();
	}
}
