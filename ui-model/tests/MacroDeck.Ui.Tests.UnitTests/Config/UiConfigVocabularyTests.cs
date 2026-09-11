using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Config.Validation;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// Regression coverage for the three frozen vocabularies of the configuration profile. A type string is what a
/// renderer switches on and an event name is what it sends; a rename or an invented member makes a field
/// unrenderable or an interaction silent, with no negotiation signal either way. A property constant nobody
/// emits is worse still - a public string with no meaning that can never be removed.
///
/// <para>
/// The expected members below are <b>hard-coded literals</b>, never read from the constants under test.
/// Deriving them would make the test agree with any rename, which is the one thing it exists to catch.
/// </para>
/// </summary>
[TestFixture]
public class UiConfigVocabularyTests
{
	/// <summary>The twenty-seven control names the existing parameter mapping produces, in the order this
	/// package declares them.</summary>
	private static readonly string[] _expectedInputs =
	[
		"string", "number", "boolean", "choice", "password", "secret", "dynamic-choice", "autocomplete",
		"multiselect", "color", "file", "folder", "hotkey", "duration", "datetime", "json", "code", "keyvalue",
		"object", "array", "ipaddress", "url", "icon", "image", "keyboard-sequence", "keyboard-combo",
		"widget-target",
	];

	/// <summary>The seven high-level controls, which name editors the application already ships rather than a
	/// parameter type. They follow the parameter counterparts and precede the chrome.</summary>
	private static readonly string[] _expectedMacroDeckInputs =
	[
		"actions-list-editor", "action-picker", "variable-picker", "device-picker", "integration-picker",
		"icon-display", "state-mapping-editor",
	];

	/// <summary>The seventeen chrome names, in the order this package declares them.</summary>
	private static readonly string[] _expectedChrome =
	[
		"flow", "step", "stack", "tabs", "tab", "heading", "prose", "instructions", "instruction",
		"copy-value", "link", "advanced-section", "divider", "banner", "validation-message", "busy", "button",
	];

	/// <summary>The three regions of a widget's configuration surface, which follow the chrome.</summary>
	private static readonly string[] _expectedWidgetChrome =
	[
		"widget-configuration", "widget-properties", "widget-editor",
	];

	private static readonly string[] _expectedEvents =
	[
		"change", "submit", "cancel", "back", "activate", "expand", "collapse", "open", "filter", "reload",
		"add", "remove",
	];

	/// <summary>Regularised spellings a reasonable person would reach for and this vocabulary must not
	/// have, because the control names it was derived from do not.</summary>
	private static readonly string[] _forbiddenPrimitives =
	[
		"select", "multi-select", "date-time", "key-value",
		"ip-address"
	];

	/// <summary>DOM event names. <c>focus</c> above all: focus is per-client state the tree must never
	/// carry, and an event reporting it is the same leak arriving from the other direction.</summary>
	private static readonly string[] _forbiddenEvents = ["click", "input", "blur", "focus"];

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	[Test]
	public void The_configuration_primitive_set_is_the_control_names_the_mapping_produces_then_macro_decks_own()
	{
		var expected = _expectedInputs
			.Concat(_expectedMacroDeckInputs)
			.Concat(_expectedChrome)
			.Concat(_expectedWidgetChrome)
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(UiConfigPrimitives.WellKnown,
				Is.EqualTo(expected).AsCollection,
				"the primitive vocabulary is the 27 control names, then the 7 high-level controls, then the " +
				"17 chrome names, then the 3 widget configuration regions, in order");
			Assert.That(UiConfigPrimitives.WellKnown, Has.Count.EqualTo(54));
			Assert.That(UiConfigPrimitives.WellKnown.Distinct(StringComparer.Ordinal).Count(),
				Is.EqualTo(54));

			foreach (var forbidden in _forbiddenPrimitives)
			{
				Assert.That(UiConfigPrimitives.WellKnown,
					Does.Not.Contain(forbidden),
					$"'{forbidden}' is a regularised spelling the existing control names do not use");
			}
		});
	}

	[Test]
	public void The_event_vocabulary_is_the_twelve_frozen_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiConfigEvents.WellKnown, Is.EqualTo(_expectedEvents).AsCollection);
			Assert.That(UiConfigEvents.WellKnown, Has.Count.EqualTo(12));

			foreach (var forbidden in _forbiddenEvents)
			{
				Assert.That(UiConfigEvents.WellKnown, Does.Not.Contain(forbidden));
			}
		});
	}

	[Test]
	public void Every_declared_property_key_is_reachable_from_at_least_one_rendered_primitive()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(), BuildEveryPrimitive());

		var renderedTypes = new HashSet<string>(StringComparer.Ordinal);
		var renderedKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var node in Walk(tree.Root))
		{
			renderedTypes.Add(node.Type);

			foreach (var key in node.Properties.Keys)
			{
				renderedKeys.Add(key);
			}
		}

		Assert.Multiple(() =>
		{
			// Both directions: an unemitted declared key and an undeclared emitted key each fail.
			Assert.That(renderedKeys, Is.EquivalentTo(UiConfigProperties.WellKnown));
			Assert.That(renderedTypes,
				Is.EquivalentTo(UiConfigPrimitives.WellKnown),
				"the fixture has to author one element of every type for the key union to be complete");
		});
	}

	[Test]
	public void A_multiple_selection_authored_without_the_reorderable_flag_emits_the_same_node_as_before()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			Configure(WithOptions(new UiMultiSelectInput { Key = "devices" })));

		var node = Walk(tree.Root).Single(n => n.Type == UiConfigPrimitives.MultiSelect);

		Assert.That(node.Properties.Keys, Does.Not.Contain(UiConfigProperties.Reorderable));
	}

	[Test]
	public void A_reorderable_multiple_selection_emits_the_flag_on_the_multiselect_node()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			Configure(WithOptions(new UiMultiSelectInput { Key = "devices", Reorderable = true })));

		var node = Walk(tree.Root).Single(n => n.Type == UiConfigPrimitives.MultiSelect);

		Assert.That(node.Properties[UiConfigProperties.Reorderable].GetBoolean(), Is.True);
	}

	/// <summary>
	/// One element of each of the forty-one types, with every property the DSL exposes populated. The option
	/// properties are set explicitly rather than through a <see cref="UiOptionsState" />, because
	/// <c>loading</c> and <c>error</c> are present only while a load is running or has failed - driving them
	/// from a state would make this fixture depend on a load's timing.
	/// </summary>
	private static UiFlow BuildEveryPrimitive()
		=> new()
		{
			Key = "setup",
			Title = "Setup",
			StepId = "credentials",
			State = "step",
			CanSubmit = true,
			Events = [UiEventHandler.On(UiConfigEvents.Submit, () => { })],
			Children =
			[
				new UiStep
				{
					Key = "credentials",
					StepId = "credentials",
					Title = "Credentials",
					Description = "Fill these in.",
					Children =
					[
						Configure(new UiStringInput { Key = "text", Multiline = true }),
						Configure(new UiNumberInput
						{
							Key = "count",
							Min = 1,
							Max = 10,
							Step = 0.5,
							ShowSlider = true,
						}),
						Configure(new UiBooleanInput
						{
							Key = "toggle",
							Segmented = true,
							FalseLabel = "Off",
							TrueLabel = "On",
						}),
						Configure(WithOptions(new UiChoiceInput
							{ Key = "choice", Segmented = true, HideLabel = true })),
						Configure(WithOptions(new UiChoiceInput { Key = "cardChoice", Cards = true })),
						Configure(new UiPasswordInput { Key = "password" }),
						Configure(new UiSecretInput { Key = "secret" }),
						Configure(WithOptions(new UiDynamicChoiceInput { Key = "dynamicChoice" })),
						Configure(WithOptions(new UiAutocompleteInput { Key = "autocomplete" })),
						Configure(WithOptions(new UiMultiSelectInput { Key = "multiSelect" })),
						Configure(WithOptions(new UiMultiSelectInput { Key = "orderedMultiSelect", Reorderable = true })),
						Configure(new UiColorInput { Key = "color" }),
						Configure(new UiFileInput
							{ Key = "file", FileExtensions = UiValue.Of<IReadOnlyList<string>>(["txt"]) }),
						Configure(new UiFolderInput { Key = "folder" }),
						Configure(new UiHotkeyInput { Key = "hotkey" }),
						Configure(new UiDurationInput { Key = "timeout", Min = 0, Max = 1000 }),
						Configure(new UiDateTimeInput { Key = "when" }),
						Configure(new UiJsonInput { Key = "payload" }),
						Configure(new UiCodeInput { Key = "script", Language = "lua" }),
						Configure(new UiKeyValueInput { Key = "headers" }),
						Configure(new UiObjectInput { Key = "endpoint" }),
						Configure(new UiArrayInput { Key = "items" }),
						Configure(new UiIpAddressInput { Key = "host" }),
						Configure(new UiUrlInput { Key = "link", AutoPrefixHttps = true }),
						Configure(new UiIconInput { Key = "icon" }),
						Configure(new UiImageInput
							{ Key = "picture", FileExtensions = UiValue.Of<IReadOnlyList<string>>(["png"]) }),
						Configure(new UiKeyboardSequenceInput { Key = "sequence" }),
						Configure(new UiKeyboardComboInput { Key = "combo" }),
						Configure(WithOptions(new UiWidgetTargetInput { Key = "widget" })),
						Configure(new UiActionsListEditor
						{
							Key = "flows",
							Triggers = UiValue.Of<IReadOnlyList<string>>(["press"]),
							CanRun = true,
						}),
						Configure(new UiActionPickerInput
							{ Key = "action", IntegrationId = "com.example.demo" }),
						Configure(new UiVariablePickerInput
						{
							Key = "variable",
							VariableTypes = UiValue.Of<IReadOnlyList<string>>(["number"]),
							WritableOnly = true,
						}),
						Configure(new UiDevicePickerInput { Key = "device" }),
						Configure(new UiIntegrationPickerInput
							{ Key = "integration", Capability = "music-player", ConfigurationEntries = true }),
						Configure(new UiIconDisplayInput
						{
							Key = "iconDisplay",
							Icon = new UiIconReference("icon-pack", "logo"),
							AspectRatio = 1.5,
							Background = "#101010",
						}),
						Configure(new UiStateMappingEditorInput
						{
							Key = "stateMapping",
							States = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("off", "Off")]),
						}),
						new UiTabs
						{
							Key = "tabs",
							Children = [new UiTab { Key = "tab", Label = "Tab" }],
						},
						new UiWidgetConfiguration
						{
							Key = "configuration",
							Properties = new UiWidgetProperties { Key = "properties" },
							Editor = new UiWidgetEditor { Key = "editor" },
						},
						new UiConfigStack
						{
							Key = "row", Direction = "horizontal", Wrap = false, RowWeight = 1,
						},
						new UiHeading { Key = "heading", Text = "Configuration" },
						new UiProse { Key = "prose", Text = "Some explanation." },
						new UiInstructions
						{
							Key = "steps",
							Children = [new UiInstruction { Key = "first", Text = "Open the portal:" }],
						},
						new UiCopyValue { Key = "redirect", Label = "Redirect URI", Value = "https://localhost" },
						new UiLink { Key = "docs", Label = "Docs", Url = "https://example.com" },
						new UiAdvancedSection
						{
							Key = "advanced",
							Label = "Advanced configuration",
							DefaultExpanded = true,
							ClearOnCollapse = true,
						},
						new UiDivider { Key = "rule" },
						new UiBanner { Key = "notice", Severity = "warning", Text = "Careful." },
						new UiValidationMessage { Key = "message", Text = "Required.", For = "text" },
						new UiBusy { Key = "busy", Text = "Loading..." },
						new UiConfigButton { Key = "apply", Label = "Apply preset", Icon = "plus" },
					],
				},
			],
		};

	/// <summary>Populates the sixteen shared input properties, so the union covers every one of them. Dispatched
	/// on the value type rather than on each of the twenty-seven records, because the shared properties are
	/// shared.</summary>
	private static UiElement Configure(UiElement input)
		=> input switch
		{
			UiInput<string> typed => Shared(typed, "value"),
			UiInput<double> typed => Shared(typed, 1d),
			UiInput<bool> typed => Shared(typed, true),
			UiInput<JsonElement> typed => Shared(typed,
				JsonSerializer.SerializeToElement(new { key = "value" })),
			UiInput<IReadOnlyList<string>> typed => Shared(typed, UiValue.Of<IReadOnlyList<string>>(["a"])),
			UiInput<IReadOnlyDictionary<string, string>> typed => Shared(typed,
				UiValue.Of<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["a"] = "b" })),
			UiInput<UiIconDisplay> typed => Shared(typed,
				new UiIconDisplay(Fit: "cover", Zoom: 150, OffsetX: 5, OffsetY: -5, Opacity: 80)),
			_ => throw new InvalidOperationException($"Unhandled input value type on '{input.Key}'."),
		};

	private static UiInput<TValue> Shared<TValue>(UiInput<TValue> input, UiValue<TValue> sample)
		=> input with
		{
			Binding = Bind.ReadOnly(sample),
			Label = "Label",
			Description = "Description",
			Placeholder = "Placeholder",
			DefaultValue = sample,
			Required = true,
			Disabled = true,
			LiteralOnly = true,
			SupportsReset = true,
			Transient = true,
			ValidationRegex = ".*",
			MaxLength = 100,
			VisibleWhen = new UiVisibleWhen { ParameterName = "text", Values = ["t"] },
			Invalid = true,
			ValidationMessage = "Not acceptable.",
			ValidationRules = [UiValidationRule.Require("Never satisfied.", () => false)],
			Events = [UiEventHandler.On(UiConfigEvents.Change, () => { })],
		};

	/// <summary>Populates the eight option properties.</summary>
	private static UiElement WithOptions(UiElement input)
		=> input switch
		{
			UiOptionsInput<string> typed => Options(typed),
			UiOptionsInput<IReadOnlyList<string>> typed => Options(typed),
			_ => throw new InvalidOperationException($"'{input.Key}' is not an option-bearing input."),
		};

	private static UiOptionsInput<TValue> Options<TValue>(UiOptionsInput<TValue> input)
		=> input with
		{
			Options = UiValue.Of<IReadOnlyList<UiOption>>([UiOption.Of("a", "A") with { Icon = "align-left" }]),
			OptionsSourceId = "source",
			DynamicOptions = true,
			AllowsCustomValue = true,
			Loading = true,
			Error = "Could not load.",
			CacheSeconds = 30,
			FilterDebounceMilliseconds = 250,
		};

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}
}
