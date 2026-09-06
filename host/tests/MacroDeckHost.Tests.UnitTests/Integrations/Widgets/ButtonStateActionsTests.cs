using MacroDeckHost.Integrations.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Integrations.Widgets;

/// <summary>
/// The "Set Button State"/"Cycle Button State" actions: every <see cref="WidgetStateWriteError" /> the
/// SDK contract defines surfaces as a real <see cref="ActionResult" /> failure (never a silent success),
/// wrapping past the last state is reported as an ordinary success (a boolean flip would never reach a
/// third state), and D7's dynamic options offer exactly the target's real state ids.
/// </summary>
[TestFixture]
public class ButtonStateActionsTests
{
	private const string WidgetId = "22222222-2222-2222-2222-222222222222";
	private static readonly string[] _awayThenHome = ["away", "home"];
	private static readonly string[] _awayThenHomeLabels = ["Away", "Home"];

	[Test]
	public async Task SetButtonState_ProviderActive_FailsWithPermissionDenied()
	{
		var widgets = new FakeWidgetApi
		{
			SetResult = WidgetStateWriteResult.Failed(WidgetStateWriteError.ProviderActive)
		};

		var result = await Execute(new SetButtonStateActionDefinition(() => widgets), widgets, state: "on");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task CycleButtonState_MappingActive_FailsWithPermissionDenied()
	{
		var widgets = new FakeWidgetApi
		{
			AdvanceResult = WidgetStateWriteResult.Failed(WidgetStateWriteError.MappingActive)
		};

		var result = await Execute(new CycleButtonStateActionDefinition(() => widgets), widgets);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task SetButtonState_UnknownState_FailsWithInvalidParameter()
	{
		var widgets = new FakeWidgetApi
		{
			SetResult = WidgetStateWriteResult.Failed(WidgetStateWriteError.UnknownState)
		};

		var result = await Execute(new SetButtonStateActionDefinition(() => widgets), widgets, state: "nope");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task SetButtonState_NotFound_FailsWithNotFound()
	{
		var widgets = new FakeWidgetApi { SetResult = WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound) };

		var result = await Execute(new SetButtonStateActionDefinition(() => widgets), widgets, state: "on");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task SetButtonState_Succeeding_ReportsSuccess()
	{
		var widgets = new FakeWidgetApi { SetResult = WidgetStateWriteResult.Succeeded("on") };

		var result = await Execute(new SetButtonStateActionDefinition(() => widgets), widgets, state: "on");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	// A boolean flip could only ever land on two faces; wrapping past the last of three (or more)
	// states and reporting it as an ordinary success is the headline case a naive on/off toggle fails.
	[Test]
	public async Task CycleButtonState_WrapsPastTheLastState_AndReportsSuccess()
	{
		var widgets = new FakeWidgetApi { AdvanceResult = WidgetStateWriteResult.Succeeded("away") };

		var result = await Execute(new CycleButtonStateActionDefinition(() => widgets), widgets);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task MissingWidgetsApi_FailsWithUnavailable()
	{
		var result = await Execute(new SetButtonStateActionDefinition(() => null), null, state: "on");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task MissingOwningWidget_FailsWithNotFound()
	{
		var widgets = new FakeWidgetApi { SetResult = WidgetStateWriteResult.Succeeded("on") };
		var action = new SetButtonStateActionDefinition(() => widgets);

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["widget"] = WidgetTargets.Self, ["state"] = "on" },
			OwnerWidgetId = null
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	// Scenario D7: real ids and labels for a stateful target, empty for a State-Mode-disabled one. A
	// hard-coded ["off", "on"] must fail this.
	[Test]
	public async Task SetStateOptions_OfferExactlyTheTargetButtonsStateIds()
	{
		const string statefulWidget = "33333333-3333-3333-3333-333333333333";
		const string disabledWidget = "44444444-4444-4444-4444-444444444444";
		var widgets = new FakeWidgetApi();
		widgets.Widgets.Add(new WidgetTargetInfo
		{
			Id = statefulWidget,
			Label = "Router",
			Location = "Main / Home",
			Type = "action-button",
			States = [new WidgetStateInfo("away", "Away"), new WidgetStateInfo("home", "Home")]
		});
		widgets.Widgets.Add(new WidgetTargetInfo
		{
			Id = disabledWidget, Label = "Momentary", Location = "Main / Home", Type = "action-button"
		});
		var action = new SetButtonStateActionDefinition(() => widgets);

		var statefulOptions = await action.GetDynamicOptionsAsync(Context(statefulWidget), CancellationToken.None);
		var disabledOptions = await action.GetDynamicOptionsAsync(Context(disabledWidget), CancellationToken.None);
		var selfOptions = await action.GetDynamicOptionsAsync(Context(WidgetTargets.Self), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(statefulOptions.Options.Select(o => o.Value), Is.EqualTo(_awayThenHome));
			Assert.That(statefulOptions.Options.Select(o => TestLocalization.Resolve(o.Label)),
				Is.EqualTo(_awayThenHomeLabels));
			Assert.That(disabledOptions.Options, Is.Empty, "a State-Mode-disabled target offers nothing to pick");
			Assert.That(selfOptions.Options, Is.Empty);
			Assert.That(selfOptions.AllowsCustomValue, Is.True, "$self still permissively accepts a typed value");
		});
	}

	private static DynamicOptionsContext Context(string target)
		=> new()
		{
			ParameterName = "state", CurrentParameters = new Dictionary<string, object?> { ["widget"] = target }
		};

	private static Task<ActionResult> Execute(IActionDefinition action, FakeWidgetApi? widgets, string? state = null)
	{
		var parameters = new Dictionary<string, object> { ["widget"] = WidgetId };
		if (state is not null)
		{
			parameters["state"] = state;
		}

		return action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public List<WidgetTargetInfo> Widgets { get; } = [];

		public WidgetStateWriteResult SetResult { get; set; }
			= WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);

		public WidgetStateWriteResult AdvanceResult { get; set; } =
			WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);

		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => Widgets;

		public bool Exists(string widgetId) => Widgets.Any(w => w.Id == widgetId);

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(SetResult);

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(AdvanceResult);
	}
}
