using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventSubscriptionMatcherTests
{
	private static readonly EventDefinitionDescriptor _sceneChanged = new(QualifiedId.Parse("obs::scene-changed"),
		"obs",
		"OBS Studio",
		true,
		new EventDefinition
		{
			Id = "scene-changed",
			Name = "Scene Changed",
			ConfigurationParameters = [ActionParameter.Text("sceneName", label: "Scene")],
			PayloadParameters = [ActionParameter.Text("sceneName", label: "Scene")]
		});

	private static readonly EventDefinitionDescriptor _valueChanged = new(QualifiedId.Parse("var::value-changed"),
		"var",
		"Variables",
		true,
		new EventDefinition
		{
			Id = "value-changed",
			Name = "Variable Changed",
			ConfigurationParameters = [ActionParameter.Number("value", label: "Value")],
			PayloadParameters = [ActionParameter.Number("value", label: "Value")]
		});

	private static readonly EventDefinitionDescriptor _targetedSceneChanged = new(
		QualifiedId.Parse("obs::scene-changed"),
		"obs",
		"OBS Studio",
		true,
		new EventDefinition
		{
			Id = "scene-changed",
			Name = "Scene Changed",
			ConfigurationParameters =
			[
				ActionParameter.Text("configuration", required: true),
				ActionParameter.Text("sceneName")
			],
			PayloadParameters =
			[
				ActionParameter.Text("configuration", required: true),
				ActionParameter.Text("sceneName")
			]
		});

	private static EventSubscriptionMatcher Matcher() => new(new ActionConditionEvaluator(Renderer()));

	private static VariableTemplateRenderer Renderer(params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		return new VariableTemplateRenderer(registry);
	}

	private static VariableContext Context(
		IReadOnlyDictionary<string, object?> eventParameters,
		params VariableEntity[] vars)
		=> Renderer(vars).CreateContextAsync(VariableScope.Global, null)
			.GetAwaiter()
			.GetResult()
			.WithEvent(eventParameters);

	private static EventConfigurationValue Config(string json, string? op = null)
		=> new(JsonDocument.Parse(json).RootElement.Clone(), op);

	private static EventSubscription Subscription(string? configuredScene, string? filterJson = null, string? op = null)
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal);
		if (configuredScene is not null)
		{
			configuration["sceneName"] = Config($"\"{configuredScene}\"", op);
		}

		JsonElement? filter = filterJson is null
			? null
			: JsonDocument.Parse(filterJson).RootElement.Clone();

		return new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			filter);
	}

	private static Dictionary<string, object?> Payload(string sceneName)
		=> new(StringComparer.Ordinal) { ["sceneName"] = sceneName };

	private static EventOccurrence Occurrence(string sceneName, EventTarget? target = null)
		=> new("obs::scene-changed", Payload(sceneName), target);

	private static EventSubscription NumericSubscription(JsonElement configuredValue, string? op = null)
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["value"] = new(configuredValue, op)
		};

		return new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"var::value-changed",
			configuration,
			null);
	}

	private static Dictionary<string, object?> NumericPayload(double value)
		=> new(StringComparer.Ordinal) { ["value"] = value };

	private static EventOccurrence NumericOccurrence(double value, EventTarget? target = null)
		=> new("var::value-changed", NumericPayload(value), target);

	// B2-B5: state operator filters on the "sceneName" payload parameter, configured by operator alone (no
	// configured value).
	private static EventSubscription StateFilterSubscription(string op, string configuredJson = "null")
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["sceneName"] = Config(configuredJson, op)
		};

		return new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			null);
	}

	private static Dictionary<string, object?> Omitted() => new(StringComparer.Ordinal);

	[Test]
	public void IsNotEmpty_filter_matches_only_a_non_empty_delivered_parameter()
	{
		var subscription = StateFilterSubscription("isNotEmpty");

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload("Live"))), Is.True);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload(""))), Is.False);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Omitted())), Is.False);
		});
	}

	[Test]
	public void IsEmpty_filter_matches_only_an_empty_delivered_parameter()
	{
		var subscription = StateFilterSubscription("isEmpty");

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload("Live"))), Is.False);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload(""))), Is.True);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Omitted())), Is.False);
		});
	}

	[Test]
	public void IsNotAvailable_filter_matches_only_an_omitted_parameter()
	{
		var subscription = StateFilterSubscription("isNotAvailable");

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload("Live"))), Is.False);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload(""))), Is.False);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Omitted())), Is.True);
		});
	}

	// B3 - isolated and named for the behaviour it changes: an isNotAvailable filter matches an occurrence
	// that omits the parameter entirely, where an equality filter on the same shape never would.
	[Test]
	public void IsNotAvailable_filter_matches_when_the_occurrence_omits_the_parameter_entirely()
	{
		var subscription = StateFilterSubscription("isNotAvailable");

		Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Omitted())), Is.True);
	}

	[Test]
	public void An_equality_filter_does_not_match_when_the_occurrence_omits_the_parameter()
	{
		var subscription = Subscription("Live");

		Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Omitted())), Is.False);
	}

	// B4 - a state operator on a different parameter must not rescue a subscription that failed the
	// required-configuration guard.
	[Test]
	public void A_state_filter_on_a_different_parameter_does_not_rescue_a_missing_required_configuration()
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["sceneName"] = Config("null", "isNotAvailable")
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			null);

		Assert.That(Matcher().Matches(subscription, _targetedSceneChanged, Context(Omitted())), Is.False);
	}

	// B5 - Matches and QuickReject must not drift: whenever Matches is true, QuickReject must be false.
	[Test]
	public void QuickReject_never_rejects_an_occurrence_that_Matches_a_state_filter()
	{
		string[] stateOperators = ["isEmpty", "isNotEmpty", "isAvailable", "isNotAvailable"];
		(string Label, string Json)[] configuredShapes = [("absent", "null"), ("leftover literal", "\"Live\"")];
		(string Label, Dictionary<string, object?> Payload)[] occurrenceShapes =
		[
			("present Live", new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Live" }),
			("present empty", new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "" }),
			("absent", Omitted())
		];

		foreach (var op in stateOperators)
		{
			foreach (var (configuredLabel, configuredJson) in configuredShapes)
			{
				foreach (var (occurrenceLabel, payload) in occurrenceShapes)
				{
					var subscription = StateFilterSubscription(op, configuredJson);
					var matches = Matcher().Matches(subscription, _sceneChanged, Context(payload));
					var occurrence = new EventOccurrence("obs::scene-changed", payload, null);
					var quickReject = EventSubscriptionMatcher.QuickReject(subscription, occurrence);

					if (matches)
					{
						Assert.That(quickReject,
							Is.False,
							$"{op} / configured={configuredLabel} / occurrence={occurrenceLabel}: " +
							"Matches was true but QuickReject rejected it");
					}
				}
			}
		}

		// The load-bearing row, spelled out as literals: a leftover literal "Live" configured under isEmpty,
		// with an occurrence that actually delivers "" - Matches must fire and QuickReject must not have
		// pre-emptively rejected it based on the stale literal.
		var literalSubscription = StateFilterSubscription("isEmpty", "\"Live\"");
		var literalOccurrence = Occurrence("");
		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(literalSubscription, _sceneChanged, Context(Payload(""))), Is.True);
			Assert.That(EventSubscriptionMatcher.QuickReject(literalSubscription, literalOccurrence), Is.False);
		});
	}

	[Test]
	public void An_unset_configuration_parameter_matches_every_occurrence()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(Subscription(null), _sceneChanged, Context(Payload("Live"))), Is.True);
			Assert.That(Matcher().Matches(Subscription(""), _sceneChanged, Context(Payload("Break"))), Is.True);
		});
	}

	[Test]
	public void A_configured_parameter_narrows_to_a_matching_occurrence()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(Subscription("Live"), _sceneChanged, Context(Payload("Live"))), Is.True);
			Assert.That(Matcher().Matches(Subscription("Live"), _sceneChanged, Context(Payload("Break"))), Is.False);
		});
	}

	[Test]
	public void A_required_target_never_treats_a_legacy_empty_selection_as_a_wildcard()
	{
		var aId = Guid.NewGuid().ToString("D");
		var bId = Guid.NewGuid().ToString("D");

		EventSubscription Targeted(string? configuration)
		{
			var values = new Dictionary<string, EventConfigurationValue>();
			if (configuration is not null)
			{
				values["configuration"] = Config($"\"{configuration}\"");
			}

			values["sceneName"] = Config("\"\"");
			return new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
				"t1",
				"obs::scene-changed",
				values,
				null);
		}

		var aPayload = new Dictionary<string, object?>
		{
			["configuration"] = aId,
			["sceneName"] = "Live"
		};
		var bPayload = new Dictionary<string, object?>
		{
			["configuration"] = bId,
			["sceneName"] = "Break"
		};

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(Targeted(null), _targetedSceneChanged, Context(aPayload)), Is.False);
			Assert.That(Matcher().Matches(Targeted(string.Empty), _targetedSceneChanged, Context(aPayload)), Is.False);
			Assert.That(Matcher().Matches(Targeted(aId), _targetedSceneChanged, Context(aPayload)), Is.True);
			Assert.That(Matcher().Matches(Targeted(aId), _targetedSceneChanged, Context(bPayload)), Is.False);
		});
	}

	[Test]
	public void A_configuration_parameter_may_be_a_variable_reference()
	{
		var target = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "target_scene",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Live"
		};

		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["sceneName"] = Config("""{"$var":"target_scene"}""")
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			null);

		var matcher = new EventSubscriptionMatcher(new ActionConditionEvaluator(Renderer(target)));

		Assert.That(matcher.Matches(subscription, _sceneChanged, Context(Payload("Live"), target)), Is.True);
	}

	[Test]
	public void A_filter_expression_is_evaluated_with_the_payload_in_scope()
	{
		const string filter =
			"""{"kind":"compare","id":"c1","left":{"$event":"sceneName"},"operator":"startsWith","right":"Br"}""";

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(Subscription(null, filter), _sceneChanged, Context(Payload("Break"))),
				Is.True);
			Assert.That(Matcher().Matches(Subscription(null, filter), _sceneChanged, Context(Payload("Live"))),
				Is.False);
		});
	}

	[Test]
	public void A_configured_parameter_and_a_filter_must_both_hold()
	{
		const string filter =
			"""{"kind":"compare","id":"c1","left":{"$event":"sceneName"},"operator":"==","right":"Break"}""";

		Assert.That(Matcher().Matches(Subscription("Live", filter), _sceneChanged, Context(Payload("Live"))),
			Is.False);
	}

	[Test]
	public void A_configuration_parameter_with_no_matching_payload_parameter_does_not_filter()
	{
		var descriptor = _sceneChanged with
		{
			Definition = new EventDefinition
			{
				Id = "scene-changed",
				Name = "Scene Changed",
				ConfigurationParameters = [ActionParameter.Text("everySeconds")],
				PayloadParameters = []
			}
		};

		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["everySeconds"] = Config("30")
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			null);

		Assert.That(Matcher().Matches(subscription, descriptor, Context(Payload("Live"))), Is.True);
	}

	[Test]
	public void QuickReject_rejects_a_literal_that_cannot_match()
	{
		Assert.That(EventSubscriptionMatcher.QuickReject(Subscription("Live"), Occurrence("Break")), Is.True);
	}

	[Test]
	public void QuickReject_accepts_a_matching_or_unfiltered_subscription()
	{
		Assert.Multiple(() =>
		{
			Assert.That(EventSubscriptionMatcher.QuickReject(Subscription("Live"), Occurrence("Live")), Is.False);
			Assert.That(EventSubscriptionMatcher.QuickReject(Subscription(null), Occurrence("Break")), Is.False);
			Assert.That(EventSubscriptionMatcher.QuickReject(Subscription(""), Occurrence("Break")), Is.False);
		});
	}

	[TestCase(null)]
	[TestCase("==")]
	[TestCase(">")]
	[TestCase("<")]
	public void QuickReject_never_rejects_a_variable_reference(string? op)
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["sceneName"] = Config("""{"$var":"target_scene"}""", op)
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			configuration,
			null);

		Assert.That(EventSubscriptionMatcher.QuickReject(subscription, Occurrence("Break")), Is.False);
	}

	[Test]
	public void QuickReject_never_rejects_a_targeted_occurrence()
	{
		var target = new EventTarget(EventTriggerOwner.ForWidget(Guid.NewGuid()), "t1");
		var occurrence = Occurrence("Break", target);

		Assert.That(EventSubscriptionMatcher.QuickReject(Subscription("Live"), occurrence), Is.False);
	}

	[Test]
	public void QuickReject_never_rejects_a_liquid_template()
	{
		var target = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "target_scene",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Live"
		};

		var subscription = Subscription("{{ vars.target_scene }}");
		var matcher = new EventSubscriptionMatcher(new ActionConditionEvaluator(Renderer(target)));

		Assert.Multiple(() =>
		{
			Assert.That(EventSubscriptionMatcher.QuickReject(subscription, Occurrence("Live")), Is.False);
			Assert.That(matcher.Matches(subscription, _sceneChanged, Context(Payload("Live"), target)), Is.True);

			Assert.That(EventSubscriptionMatcher.QuickReject(subscription, Occurrence("Break")), Is.False);
			Assert.That(matcher.Matches(subscription, _sceneChanged, Context(Payload("Break"), target)), Is.False);
		});
	}

	[Test]
	public void QuickReject_ignores_configuration_that_is_not_a_payload_parameter()
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["expression"] = Config("\"0 9 * * *\"")
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"time::cron",
			configuration,
			null);

		Assert.That(EventSubscriptionMatcher.QuickReject(subscription, Occurrence("Break")), Is.False);
	}

	[TestCase("==", 50, 50, true)]
	[TestCase("==", 50, 60, false)]
	[TestCase("!=", 50, 60, true)]
	[TestCase("!=", 50, 50, false)]
	[TestCase(">", 50, 60, true)]
	[TestCase(">", 50, 50, false)]
	[TestCase(">", 50, 40, false)]
	[TestCase("<", 50, 40, true)]
	[TestCase("<", 50, 50, false)]
	[TestCase(">=", 50, 50, true)]
	[TestCase(">=", 50, 49, false)]
	[TestCase("<=", 50, 50, true)]
	[TestCase("<=", 50, 51, false)]
	public void Matches_and_QuickReject_agree_on_every_operator(string op,
		double configured,
		double actual,
		bool shouldMatch)
	{
		foreach (var configuredAsString in new[] { false, true })
		{
			var configuredJson = configuredAsString
				? $"\"{configured.ToString(CultureInfo.InvariantCulture)}\""
				: configured.ToString(CultureInfo.InvariantCulture);
			var configuredValue = JsonDocument.Parse(configuredJson).RootElement.Clone();

			var subscription = NumericSubscription(configuredValue, op);
			var occurrence = NumericOccurrence(actual);
			var context = Context(NumericPayload(actual));

			Assert.Multiple(() =>
			{
				Assert.That(Matcher().Matches(subscription, _valueChanged, context),
					Is.EqualTo(shouldMatch),
					$"Matches disagreed for {op} (configured as string: {configuredAsString})");
				Assert.That(EventSubscriptionMatcher.QuickReject(subscription, occurrence),
					Is.EqualTo(!shouldMatch),
					$"QuickReject disagreed for {op} (configured as string: {configuredAsString})");
			});
		}
	}

	[Test]
	public void Configured_greater_than_fifty_fires_for_the_payload_being_greater_not_the_configured_value()
	{
		var subscription = NumericSubscription(JsonDocument.Parse("50").RootElement.Clone(), ">");

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(60))), Is.True);
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(40))), Is.False);
		});
	}

	[Test]
	public void An_absent_operator_still_means_equality()
	{
		var subscription = NumericSubscription(JsonDocument.Parse("50").RootElement.Clone());

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(50))), Is.True);
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(60))), Is.False);
			Assert.That(EventSubscriptionMatcher.QuickReject(subscription, NumericOccurrence(50)), Is.False);
			Assert.That(EventSubscriptionMatcher.QuickReject(subscription, NumericOccurrence(60)), Is.True);
		});
	}

	[TestCase(">")]
	[TestCase("<")]
	[TestCase(">=")]
	public void An_unset_value_matches_every_occurrence_whatever_the_operator(string op)
	{
		var subscription = NumericSubscription(JsonDocument.Parse("\"\"").RootElement.Clone(), op);

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(1))), Is.True);
			Assert.That(EventSubscriptionMatcher.QuickReject(subscription, NumericOccurrence(1)), Is.False);
		});
	}

	[Test]
	public void An_unknown_operator_falls_back_to_equality_rather_than_never_firing()
	{
		var configuration = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
		{
			["value"] = new(JsonDocument.Parse("50").RootElement.Clone(), "≥")
		};
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"var::value-changed",
			configuration,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(50))), Is.True);
			Assert.That(Matcher().Matches(subscription, _valueChanged, Context(NumericPayload(60))), Is.False);
		});
	}

	[Test]
	public void A_greater_than_operator_against_a_non_numeric_payload_falls_back_to_an_ordinal_string_compare()
	{
		var subscription = Subscription("Live", op: ">");

		Assert.Multiple(() =>
		{
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload("Zulu"))), Is.True);
			Assert.That(Matcher().Matches(subscription, _sceneChanged, Context(Payload("Break"))), Is.False);
		});
	}
}
