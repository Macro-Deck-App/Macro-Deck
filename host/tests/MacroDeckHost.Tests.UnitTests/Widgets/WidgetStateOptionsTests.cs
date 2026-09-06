using MacroDeckHost.Integrations.Widgets;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetStateOptionsTests
{
	private const string _twoStateWidget = "44444444-4444-4444-4444-444444444444";
	private const string _singleStateWidget = "55555555-5555-5555-5555-555555555555";
	private const string _unknownWidget = "66666666-6666-6666-6666-666666666666";

	private FakeWidgetApi _widgets = null!;
	private WidgetIntegration _integration = null!;

	[SetUp]
	public async Task SetUp()
	{
		_widgets = new FakeWidgetApi();
		// Deliberately not "off"/"on": proves the options come from the widget's real states, not a
		// hard-coded legacy vocabulary the implementation happens to still recognize.
		_widgets.Widgets.Add(new WidgetTargetInfo
		{
			Id = _twoStateWidget,
			Label = "Toggle",
			Location = "Main / Home",
			Type = "action-button",
			States = [new WidgetStateInfo("left", "Left"), new WidgetStateInfo("right", "Right")]
		});
		_widgets.Widgets.Add(new WidgetTargetInfo
		{
			Id = _singleStateWidget,
			Label = "Button",
			Location = "Main / Home",
			Type = "action-button"
		});

		_integration = new WidgetIntegration();
		await _integration.InitializeAsync(new FakeIntegrationContext(_widgets));
	}

	[Test]
	public async Task TwoStateTarget_OffersAllFourChoices()
	{
		var options = await Resolve(_twoStateWidget);

		Assert.That(options.Select(o => o.Value), Is.EquivalentTo(["current", "both", "left", "right"]));
	}

	[Test]
	public async Task SingleStateTarget_OffersOnlyCurrent()
	{
		var options = await Resolve(_singleStateWidget);

		Assert.That(options.Select(o => o.Value), Is.EqualTo(["current"]));
	}

	[Test]
	public async Task SelfTarget_StaysPermissiveButDropsTheLegacyVocabulary_BecauseItResolvesOnlyAtRunTime()
	{
		var options = await Resolve(WidgetTargets.Self);

		// $self only resolves at run time, so the picker still cannot ask the target what states it
		// has and stays permissive - but it must not offer "on"/"off" as if every widget had exactly
		// those two faces. Every already-stored "on"/"off" value keeps resolving regardless.
		Assert.That(options.Select(o => o.Value), Is.EquivalentTo(["current", "both"]));
	}

	[Test]
	public async Task EmptyTarget_StaysPermissiveButDropsTheLegacyVocabulary_BecauseThereIsNoWidgetToAsk()
	{
		var options = await Resolve(string.Empty);

		Assert.That(options.Select(o => o.Value), Is.EquivalentTo(["current", "both"]));
	}

	// Unlike $self or an empty target (which resolve only at run time, so "current"/"both" stay
	// permissively offered even though the legacy on/off vocabulary is dropped - see the tests above),
	// a specifically referenced but missing widget id is a stale reference: its real states cannot be
	// guessed, so nothing beyond "current" is offered rather than presenting "both" as if the target
	// still had some knowable set of faces.
	[Test]
	public async Task UnknownTarget_OffersOnlyCurrent_RatherThanGuessingALegacyVocabulary()
	{
		var options = await Resolve(_unknownWidget);

		Assert.That(options.Select(o => o.Value), Is.EqualTo(["current"]));
	}


	private async Task<IReadOnlyList<ActionParameterOption>> Resolve(string target)
	{
		var action = (IDynamicOptionsActionDefinition)_integration.Actions.Single(a => a.Id == "set-label");
		var result = await action.GetDynamicOptionsAsync(Context(target), CancellationToken.None);
		return result.Options;
	}

	private static DynamicOptionsContext Context(string target)
		=> new()
		{
			ParameterName = "state",
			CurrentParameters = new Dictionary<string, object?> { ["widget"] = target }
		};

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public List<WidgetTargetInfo> Widgets { get; } = [];

		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => Widgets;

		public bool Exists(string widgetId) => Widgets.Any(w => w.Id == widgetId);

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

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
