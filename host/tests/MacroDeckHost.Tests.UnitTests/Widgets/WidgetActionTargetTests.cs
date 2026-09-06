using MacroDeckHost.Integrations.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetActionTargetTests
{
	private const string _selfWidget = "22222222-2222-2222-2222-222222222222";
	private const string _otherWidget = "33333333-3333-3333-3333-333333333333";

	private static readonly string[] _iconFramingNumbers = ["iconZoom", "iconOffsetX", "iconOffsetY", "iconOpacity"];

	private FakeWidgetApi _widgets = null!;
	private WidgetIntegration _integration = null!;

	[SetUp]
	public async Task SetUp()
	{
		_widgets = new FakeWidgetApi();
		_integration = new WidgetIntegration();
		await _integration.InitializeAsync(new FakeIntegrationContext(_widgets));
	}

	[Test]
	public async Task SelfTarget_ResolvesToTheWidgetTheFlowBelongsTo()
	{
		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = WidgetTargets.Self,
				["label"] = "Live"
			},
			ownerWidgetId: _selfWidget);

		Assert.Multiple(() =>
		{
			Assert.That(_widgets.Applied.Single().WidgetId, Is.EqualTo(_selfWidget));
			Assert.That(_widgets.Applied.Single().Patch.Label, Is.EqualTo("Live"));
		});
	}

	[Test]
	public async Task ExplicitTarget_WinsOverTheOwningWidget()
	{
		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = _otherWidget,
				["label"] = "Live"
			},
			ownerWidgetId: _selfWidget);

		Assert.That(_widgets.Applied.Single().WidgetId, Is.EqualTo(_otherWidget));
	}

	[Test]
	public async Task SelfTarget_WithNoOwningWidget_DoesNothing()
	{
		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = WidgetTargets.Self,
				["label"] = "Live"
			},
			ownerWidgetId: null);

		Assert.That(_widgets.Applied, Is.Empty);
	}

	[Test]
	public async Task MissingTarget_FallsBackToTheOwningWidget()
	{
		await Execute("set-label",
			new Dictionary<string, object> { ["label"] = "Live" },
			ownerWidgetId: _selfWidget);

		Assert.That(_widgets.Applied.Single().WidgetId, Is.EqualTo(_selfWidget));
	}

	[TestCase("current", WidgetStates.Current)]
	[TestCase("on", "on")]
	[TestCase("off", "off")]
	[TestCase("both", WidgetStates.All)]
	public async Task StateParameter_SelectsTheAppearanceToChange(string value, string expectedStateId)
	{
		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["state"] = value,
				["label"] = "x"
			},
			ownerWidgetId: _selfWidget);

		Assert.That(_widgets.Applied.Single().StateIds, Is.EqualTo(new[] { expectedStateId }));
	}

	[Test]
	public async Task UnfilledColor_LeavesThePropertyAlone()
	{
		await Execute("set-border",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["style"] = "blink",
				["color"] = string.Empty
			},
			ownerWidgetId: _selfWidget);

		var patch = _widgets.Applied.Single().Patch;
		Assert.Multiple(() =>
		{
			Assert.That(patch.BorderStyle, Is.EqualTo("blink"));
			Assert.That(patch.BorderColor, Is.Null);
		});
	}

	[Test]
	public async Task UnfilledColor_OnSetBackgroundColor_LeavesThePropertyAloneAndClearsNothing()
	{
		await Execute("set-background-color",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["color"] = string.Empty
			},
			ownerWidgetId: _selfWidget);

		var request = _widgets.Applied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Patch.BackgroundColor, Is.Null);
			Assert.That(request.ClearProperties, Is.Empty);
		});
	}

	[Test]
	public async Task ResetSentinel_OnSetBackgroundColor_ClearsRatherThanSettingTheSentinelAsAColor()
	{
		await Execute("set-background-color",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["color"] = WidgetAppearanceValues.Reset
			},
			ownerWidgetId: _selfWidget);

		var request = _widgets.Applied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Patch.BackgroundColor, Is.Null);
			Assert.That(request.ClearProperties, Is.EqualTo(new[] { WidgetAppearanceProperty.BackgroundColor }));
		});
	}

	[Test]
	public async Task RealColor_OnSetBackgroundColor_StillSetsIt()
	{
		await Execute("set-background-color",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["color"] = "#abcdef"
			},
			ownerWidgetId: _selfWidget);

		var request = _widgets.Applied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Patch.BackgroundColor, Is.EqualTo("#abcdef"));
			Assert.That(request.ClearProperties, Is.Empty);
		});
	}

	[Test]
	public void SetIconDisplayAction_DeclaresItsNumbersUnfilled()
	{
		var parameters = _integration.Actions.Single(a => a.Id == "set-icon-display").Parameters;

		Assert.Multiple(() =>
		{
			foreach (var name in _iconFramingNumbers)
			{
				var parameter = parameters.Single(p => p.Name == name);
				Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.Number), name);
				Assert.That(parameter.DefaultValue, Is.EqualTo(WidgetActionParameters.Unchanged), name);
			}
		});
	}

	[Test]
	public async Task UnfilledIconFraming_LeavesEveryFramingPropertyAlone()
	{
		await Execute("set-icon-display",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["iconFit"] = string.Empty,
				["iconZoom"] = string.Empty,
				["iconOffsetX"] = string.Empty,
				["iconOffsetY"] = string.Empty,
				["iconOpacity"] = string.Empty
			},
			ownerWidgetId: _selfWidget);

		var request = _widgets.Applied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Patch.IsEmpty, Is.True);
			Assert.That(request.ClearProperties, Is.Empty);
		});
	}

	[Test]
	public async Task FilledIconFraming_SetsOnlyWhatWasFilledIn()
	{
		await Execute("set-icon-display",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["iconFit"] = "cover",
				["iconZoom"] = 250d,
				["iconOffsetX"] = string.Empty,
				["iconOffsetY"] = -20d,
				["iconOpacity"] = 0d
			},
			ownerWidgetId: _selfWidget);

		var patch = _widgets.Applied.Single().Patch;
		Assert.Multiple(() =>
		{
			Assert.That(patch.IconFit, Is.EqualTo("cover"));
			Assert.That(patch.IconZoom, Is.EqualTo(250));
			Assert.That(patch.IconOffsetX, Is.Null);
			Assert.That(patch.IconOffsetY, Is.EqualTo(-20));
			Assert.That(patch.IconOpacity, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task ResetSentinel_OnSetIconDisplay_ClearsTheFramingAndWritesNothing()
	{
		await Execute("set-icon-display",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["iconFit"] = WidgetAppearanceValues.Reset,
				["iconZoom"] = 250d,
				["iconOpacity"] = 30d
			},
			ownerWidgetId: _selfWidget);

		var request = _widgets.Applied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Patch.IsEmpty, Is.True);
			Assert.That(request.ClearProperties, Is.EqualTo(new[] { WidgetAppearanceProperty.IconDisplay }));
		});
	}

	[Test]
	public void SetBackgroundColorAction_ColorParameter_SupportsReset()
	{
		var color = _integration.Actions.Single(a => a.Id == "set-background-color")
			.Parameters.Single(p => p.Name == "color");

		Assert.That(color.SupportsReset, Is.True);
	}

	[TestCase("set-label-color")]
	[TestCase("set-border")]
	public void OtherWidgetColorParameters_SupportReset(string actionId)
	{
		var color = _integration.Actions.Single(a => a.Id == actionId)
			.Parameters.Single(p => p.Name == "color");

		Assert.That(color.SupportsReset, Is.True);
	}

	[Test]
	public async Task EmptyLabel_ClearsIt()
	{
		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["label"] = string.Empty
			},
			ownerWidgetId: _selfWidget);

		Assert.That(_widgets.Applied.Single().Patch.Label, Is.Empty);
	}

	[Test]
	public async Task Font_LeavesUnchangedFieldsAlone()
	{
		await Execute("set-font",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["fontFaceId"] = "inter-400-5-upright",
				["fontSize"] = 22d,
				["textAlign"] = string.Empty,
				["labelPosition"] = string.Empty
			},
			ownerWidgetId: _selfWidget);

		var patch = _widgets.Applied.Single().Patch;
		Assert.Multiple(() =>
		{
			Assert.That(patch.FontFaceId, Is.EqualTo("inter-400-5-upright"));
			Assert.That(patch.FontSize, Is.EqualTo(22d));
			Assert.That(patch.LabelColor, Is.Null);
			Assert.That(patch.TextAlign, Is.Null);
			Assert.That(patch.LabelPosition, Is.Null);
		});
	}


	[Test]
	public void EveryAppearanceAction_AsksTheSameTwoQuestions()
	{
		// Scoped to the appearance actions specifically (set-label, set-background-color, ...) - the
		// explicit state actions added by issue #612 (set-state, toggle-state) ask a different question
		// (which state to become, not which to restyle) and are not appearance actions at all.
		var appearanceActions = _integration.Actions.OfType<WidgetAppearanceActionDefinition>().ToList();

		Assert.That(appearanceActions, Is.Not.Empty);
		Assert.Multiple(() =>
		{
			foreach (var action in appearanceActions)
			{
				var names = action.Parameters.Select(p => p.Name).ToList();
				Assert.That(names, Does.Contain("widget"), action.Id);
				Assert.That(names, Does.Contain("state"), action.Id);

				// A change is an edit now; there is no second write path left to choose (issue #312).
				Assert.That(names, Does.Not.Contain("persist"), action.Id);
			}
		});
	}

	[Test]
	public void LabelParameter_IsMultilineWithTheEditorPlaceholder()
	{
		var label = _integration.Actions.Single(a => a.Id == "set-label")
			.Parameters.Single(p => p.Name == "label");

		Assert.Multiple(() =>
		{
			Assert.That(label.Type, Is.EqualTo(ActionParameterType.String));
			Assert.That(label.Multiline, Is.True);
			Assert.That(TestLocalization.Resolve(label.Placeholder), Is.EqualTo("Button label"));
		});
	}

	[Test]
	public async Task Label_WithLineBreaksAndLiquid_RoundTripsUnchanged()
	{
		const string label = "Line one\nLine two\n{{ vars.system_cpu_usage_percent }}%";

		await Execute("set-label",
			new Dictionary<string, object>
			{
				["widget"] = _selfWidget,
				["label"] = label
			},
			ownerWidgetId: _selfWidget);

		Assert.That(_widgets.Applied.Single().Patch.Label, Is.EqualTo(label));
	}

	[Test]
	public void TargetParameter_DefaultsToThisWidget()
	{
		var target = _integration.Actions[0].Parameters.Single(p => p.Name == "widget");

		Assert.Multiple(() =>
		{
			Assert.That(target.Type, Is.EqualTo(ActionParameterType.WidgetTarget));
			Assert.That(target.DefaultValue, Is.EqualTo(WidgetTargets.Self));
			Assert.That(target.OptionsSourceId, Is.EqualTo(WidgetOptionsSources.Widgets));
		});
	}

	private Task<ActionResult> Execute(string actionId, Dictionary<string, object> parameters, string? ownerWidgetId)
	{
		var action = _integration.Actions.Single(a => a.Id == actionId);
		return action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters,
				OwnerWidgetId = ownerWidgetId
			});
	}

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public List<WidgetAppearanceRequest> Applied { get; } = [];

#pragma warning disable CS0618 // Unused legacy field, kept as-is; not yet migrated to state ids.
		public List<(string WidgetId, WidgetStateSelector State)> Reset { get; } = [];
#pragma warning restore CS0618

		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => true;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		{
			Applied.Add(request);
			return Task.FromResult(true);
		}

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class FakeIntegrationContext : IIntegrationContext
	{
		public FakeIntegrationContext(IWidgetApi widgets)
		{
			Widgets = widgets;
		}

		public IWidgetApi Widgets { get; }

		public MacroDeck.Sdk.Variables.IVariableApi Variables => throw new NotSupportedException();
		public MacroDeck.Sdk.Variables.IUserVariableApi UserVariables => throw new NotSupportedException();
		public MacroDeck.Sdk.ConfigFlow.IIntegrationConfig Config => throw new NotSupportedException();
		public MacroDeck.Sdk.Decks.IDeckNavigator Deck => throw new NotSupportedException();
		public MacroDeck.Sdk.Scripts.IScriptApi Scripts => throw new NotSupportedException();
		public MacroDeck.Sdk.Events.IEventPublisher Events => throw new NotSupportedException();
		public MacroDeck.Sdk.Notifications.IUserNotifier Notifications => throw new NotSupportedException();
	}
}
