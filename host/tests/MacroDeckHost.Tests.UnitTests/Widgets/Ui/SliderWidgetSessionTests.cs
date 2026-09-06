using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Slider;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class SliderWidgetSessionTests
{
	[Test]
	public async Task An_adjust_moves_the_level_and_pushes_the_mapped_value_in_the_variables_units()
	{
		var slider = Writable(min: 0, max: 100, step: 0, value: 50);

		var result = slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.7);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(slider.Host.ById("slider.track").Number("level"), Is.EqualTo(0.7).Within(1e-9));
			Assert.That(slider.Provider.Writes, Has.Count.EqualTo(1));
			Assert.That(slider.Provider.Writes[0], Is.EqualTo(70).Within(1e-9));
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task
		A_commit_on_release_variables_adjust_moves_the_level_but_pushes_nothing_and_the_following_change_pushes_once()
	{
		var slider = Writable(min: 0, max: 100, step: 0, value: 50, commitOnRelease: true);

		var adjustResult = slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.3);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(adjustResult.IsAccepted, Is.True);
			Assert.That(slider.Host.ById("slider.track").Number("level"),
				Is.EqualTo(0.3).Within(1e-9),
				"The readout must still follow the finger even though the variable is not written yet (#469).");
			Assert.That(slider.Provider.Writes, Is.Empty);
		});

		var changeResult = slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.3);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(changeResult.IsAccepted, Is.True);
			Assert.That(slider.Provider.Writes, Has.Count.EqualTo(1));
			Assert.That(slider.Provider.Writes[0], Is.EqualTo(30).Within(1e-9));
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task An_adjust_snaps_onto_the_variables_grid_anchored_at_its_minimum()
	{
		// min 3, max 103, step 10: the retired client anchored its snap at zero - round(raw/step)*step -
		// which lands on 50, a value the variable's own grid does not contain. Anchoring at the minimum gives
		// 53. (Snapping in fraction space is not the distinction: a fraction is measured from the minimum
		// too, so it is the same arithmetic.)
		var slider = Writable(min: 3, max: 103, step: 10, value: 3);

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.47);
		await slider.Host.SettleAsync();

		Assert.That(slider.Provider.Writes.Single(), Is.EqualTo(53));

		await slider.DisposeAsync();
	}

	[Test]
	public async Task Grid_snapping_rounds_an_exact_tie_away_from_zero()
	{
		// min 0, max 10, step 1: a level of 0.25 lands exactly on raw 2.5, a tie between step 2 and 3. C#'s
		// default Math.Round would bank to 2 (even); JavaScript's half-up Math.round gives 3. The host must
		// match the client's half-up behaviour, or the same drag ends on a different value on each side.
		var slider = Writable(min: 0, max: 10, step: 1, value: 0);

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.25);
		await slider.Host.SettleAsync();

		Assert.That(slider.Provider.Writes.Single(), Is.EqualTo(3));

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_fractional_step_lands_on_a_clean_value_rather_than_a_binary_float_artefact()
	{
		// min 0, max 1, step 0.1: the seventh step is 0.7, but rebuilding it as 7 * 0.1 in binary floating
		// point gives 0.7000000000000001 - which the variable would be written with and which the readout
		// beside the track would spell out to the user.
		var slider = Writable(min: 0, max: 1, step: 0.1, value: 0);

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.7);
		await slider.Host.SettleAsync();

		Assert.That(slider.Provider.Writes.Single(), Is.EqualTo(0.7));

		await slider.DisposeAsync();
	}

	[Test]
	public async Task
		A_reading_reporting_the_old_value_during_the_hold_does_not_move_the_level_back_and_one_after_expiry_does()
	{
		var slider = Writable(min: 0, max: 100, step: 0, value: 50);

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.9);
		await slider.Host.SettleAsync();

		Assert.That(slider.Host.ById("slider.track").Number("level"),
			Is.EqualTo(0.9).Within(1e-9),
			"the change landed");

		// A reading landing while the hold is active and far from the held value must not snap the level back.
		slider.Report("40");

		Assert.That(slider.Host.ById("slider.track").Number("level"),
			Is.EqualTo(0.9).Within(1e-9),
			"a reading during the hold must not fight the value the user just set");

		// Once the hold has expired, the same stale-looking reading is adopted.
		slider.TimeProvider.Now += TimeSpan.FromMilliseconds(3001);
		slider.Report("40");

		Assert.That(slider.Host.ById("slider.track").Number("level"),
			Is.EqualTo(0.4).Within(1e-9),
			"once the hold expires the provider's own value takes over");

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_dispatch_while_the_host_is_locked_is_rejected_and_pushes_nothing()
	{
		var slider = Writable(min: 0, max: 100, step: 0, value: 50, locked: true);

		var result = slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.7);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.False, "a locked host must refuse before any state is touched");
			Assert.That(slider.Host.ById("slider.track").Number("level"),
				Is.EqualTo(0.5).Within(1e-9),
				"the level must not move");
			Assert.That(slider.Provider.Writes, Is.Empty);
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_value_change_produces_only_set_properties_operations_never_a_structural_patch()
	{
		var slider = Writable(min: 0, max: 100, step: 0, value: 50);

		slider.Host.ClearPatches();

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.2);
		await slider.Host.SettleAsync();

		slider.Report("65");

		var ops = slider.Host.Patches.SelectMany(p => p.Operations).Select(o => o.Op).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ops, Is.Not.Empty, "the scenario must actually have produced patches to assert on");
			Assert.That(ops,
				Has.All.EqualTo(UiPatchOperations.SetProperties),
				"rapid updates must patch properties in place, never rebuild the tree");
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_burst_of_dispatches_is_coalesced_but_still_ends_on_the_value_the_user_landed_on()
	{
		// A client that ignores its own throttle - or simply a slow provider - must not be able to lose the
		// last value. Intermediate values may be dropped, which is the point of coalescing; the one the
		// interaction ended on may not.
		var slider = Writable(min: 0, max: 100, step: 0, value: 0);

		for (var i = 1; i <= 10; i++)
		{
			slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, i / 10d);
		}

		// The trailing call waits out the minimum interval, so the clock has to reach it.
		for (var i = 0; i < 200 && slider.Provider.LastWrite != 100d; i++)
		{
			slider.TimeProvider.Advance(TimeSpan.FromMilliseconds(50));
			await Task.Delay(1);
		}

		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(slider.Provider.LastWrite,
				Is.EqualTo(100d),
				"the value the interaction ended on has to arrive");
			Assert.That(slider.Provider.Writes,
				Has.Count.LessThan(10),
				"ten dispatches must not be ten writes to the provider");
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_slider_follows_the_variable_with_no_interaction()
	{
		var slider = UserOwned(min: 0, max: 100, step: 1, value: "75");

		Assert.That(slider.Host.ById("slider.track").Number("level"), Is.EqualTo(0.75).Within(1e-9));

		slider.Report("30");

		Assert.That(slider.Host.ById("slider.track").Number("level"), Is.EqualTo(0.30).Within(1e-9));

		await slider.DisposeAsync();
	}

	[Test]
	public async Task An_out_of_range_variable_clamps_the_level_and_never_writes()
	{
		var slider = UserOwned(min: 0, max: 100, step: 1, value: "160");

		Assert.That(slider.Host.ById("slider.track").Number("level"), Is.EqualTo(1.0).Within(1e-9));

		// Refresh several times with no interaction at all.
		for (var i = 0; i < 3; i++)
		{
			slider.Report("160");
		}

		Assert.Multiple(() =>
		{
			Assert.That(slider.Host.ById("slider.track").Number("level"), Is.EqualTo(1.0).Within(1e-9));
			Assert.That(slider.Provider.Writes, Is.Empty, "no drag ever happened, so nothing may have been written");
		});

		await slider.DisposeAsync();
	}

	/// <summary>
	/// The variable's own volatile range is what a drag maps onto - a gain-boosted input reports a ceiling
	/// above the widget config's, and a write mapped onto the config's range instead would land somewhere
	/// the user never dragged to.
	/// </summary>
	[Test]
	public async Task A_drag_maps_onto_the_variables_own_range_not_the_widget_configs()
	{
		var slider = Writable(min: 0,
			max: 100,
			step: 1,
			value: 0,
			configMin: -60,
			configMax: 0,
			configStep: 0.5);

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.5);
		await slider.Host.SettleAsync();

		Assert.That(slider.Provider.Writes.Single(),
			Is.EqualTo(50).Within(1e-9),
			"the write must land in the variable's own units (50), never the widget config's (-30)");

		await slider.DisposeAsync();
	}

	/// <summary>A variable that declares no range of its own leaves the widget config's Min/Max/Step in
	/// place, per field - which is what a plain user variable relies on to be draggable at all.</summary>
	[Test]
	public async Task A_variable_declaring_no_range_falls_back_to_the_configured_one()
	{
		var slider = UserOwned(min: -60, max: 0, step: 0.5, value: "-10");

		slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.5);
		await slider.Host.SettleAsync();

		Assert.That(slider.Value, Is.EqualTo("-30"));

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_slider_whose_variable_declares_no_write_capability_is_inert_on_drag()
	{
		var slider = ReadOnlyIntegrationOwned(min: 0, max: 100, step: 1, value: "75");

		var result = slider.Host.ById("slider.track").Raise(UiComponentEvents.Adjust, 0.2);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.False, "an unwritable slider must refuse the drag");
			Assert.That(slider.Host.ById("slider.track").Number("level"),
				Is.EqualTo(0.75).Within(1e-9),
				"the thumb must not move and snap back");
			Assert.That(slider.Provider.Writes, Is.Empty);
			Assert.That(slider.Value, Is.EqualTo("75"));
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_slider_whose_variable_is_user_owned_writes_it_on_drag()
	{
		var slider = UserOwned(min: 0, max: 100, step: 1, value: "75");

		var result = slider.Host.ById("slider.track").Raise(UiComponentEvents.Change, 0.2);
		await slider.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(slider.Provider.Writes, Is.Empty, "a user variable is stored, never handed to a provider");
			Assert.That(slider.Value, Is.EqualTo("20"));
		});

		await slider.DisposeAsync();
	}

	[Test]
	public async Task A_non_numeric_variable_renders_a_defined_level_and_value_text_never_NaN()
		=> await AssertUnresolvedVariableRendersSafely(UserOwned(min: 0,
			max: 100,
			step: 1,
			value: "abc",
			type: DomainVariableType.Text));

	[Test]
	public async Task An_unavailable_variable_renders_a_defined_level_and_value_text_never_NaN()
	{
		var slider = UserOwned(min: 0, max: 100, step: 1, value: "75");
		slider.Registry.SetAvailable(slider.Entity.Id, false);
		slider.Report("75");

		await AssertUnresolvedVariableRendersSafely(slider);
	}

	[Test]
	public async Task A_missing_variable_renders_a_defined_level_and_value_text_never_NaN()
		=> await AssertUnresolvedVariableRendersSafely(Missing());

	/// <summary>
	/// The slider's target is stored as a plain name, which ADR 0081 keeps true of every place a variable is
	/// stored as a reference rather than written as template text. A dotted key is therefore a name nothing
	/// declares - not an attribute path - and a slider is not a way to drag a variable's unit.
	/// </summary>
	[Test]
	public async Task An_attribute_path_is_not_a_slider_target()
		=> await AssertUnresolvedVariableRendersSafely(BoundTo("vol.max"));

	private static async Task AssertUnresolvedVariableRendersSafely(SliderUnderTest slider)
	{
		var level = slider.Host.ById("slider.track").Number("level");
		var valueNode = slider.Host.ById("slider.header.value");

		Assert.Multiple(() =>
		{
			Assert.That(double.IsFinite(level!.Value), Is.True, "the level must never be NaN or infinite");
			Assert.That(level, Is.EqualTo(0.0).Within(1e-9), "an unresolved reading renders at the minimum end");
			Assert.That(valueNode.HasProperty("text"), Is.True, "the value text must be defined, never blank");
			Assert.That(LocalizedTextKey(valueNode),
				Is.EqualTo(AppStrings.Widgets.Slider.ValueUnavailable().Key),
				"an unresolved reading must fall back to the unavailable text, never a NaN-bearing value");
			Assert.That(slider.Provider.Writes, Is.Empty);
		});

		await slider.DisposeAsync();
	}

	/// <summary>The localization key a rendered <c>text</c> property resolves to - see
	/// <c>LocalizedTextJsonConverter</c> for the wire shape a localized reference (as opposed to a
	/// literal string) always takes.</summary>
	private static LocalizationKey LocalizedTextKey(UiTestNode node)
	{
		var localized = node.Property("text")!.Value.GetProperty("$localized");

		return new LocalizationKey(localized.GetProperty("scope").GetString()!,
			localized.GetProperty("key").GetString()!);
	}

	[Test]
	public async Task The_declared_event_set_does_not_depend_on_whether_the_variable_exists()
	{
		var slider = Missing();

		Assert.That(slider.Session.BuildEvents(),
			Is.Not.Empty,
			"the declared event set is a pure function of stored config, not of runtime variable state");

		await slider.DisposeAsync();
	}

	private static SliderUnderTest Writable(
		double min,
		double max,
		double step,
		double value,
		bool commitOnRelease = false,
		bool locked = false,
		double configMin = 0,
		double configMax = 100,
		double configStep = 0)
		=> SliderUnderTest.Build(entity =>
			{
				entity.Classification = VariableClassification.Integration;
				entity.OwnerIntegrationId = RecordingVariableProvider.IntegrationId;
				entity.DefinitionId = "vol";
				entity.Value = value.ToString(CultureInfo.InvariantCulture);
				entity.Min = min;
				entity.Max = max;
				entity.Step = step;
				entity.Write = new VariableWriteCapability { CommitOnRelease = commitOnRelease };
			},
			configMin,
			configMax,
			configStep,
			locked);

	private static SliderUnderTest ReadOnlyIntegrationOwned(double min, double max, double step, string value)
		=> SliderUnderTest.Build(entity =>
			{
				entity.Classification = VariableClassification.Integration;
				entity.OwnerIntegrationId = RecordingVariableProvider.IntegrationId;
				entity.DefinitionId = "vol";
				entity.Value = value;
			},
			min,
			max,
			step);

	private static SliderUnderTest UserOwned(
		double min,
		double max,
		double step,
		string value,
		DomainVariableType type = DomainVariableType.Numeric)
		=> SliderUnderTest.Build(entity =>
			{
				entity.Type = type;
				entity.Value = value;
			},
			min,
			max,
			step);

	private static SliderUnderTest Missing() => SliderUnderTest.Build(entity: null, 0, 100, 1);

	private static SliderUnderTest BoundTo(string bindingName)
		=> SliderUnderTest.Build(entity => entity.Value = "75", 0, 100, 1, bindingName: bindingName);

	/// <summary>One assembled slider: the real registry, variable service and session, with the owning
	/// provider's writes recorded at the one boundary a variable write actually leaves the host through.
	/// </summary>
	private sealed class SliderUnderTest
	{
		private const string VariableName = "vol";

		private SliderUnderTest(
			UiTestHost host,
			SliderWidgetSession session,
			RecordingVariableProvider provider,
			VariableRegistry registry,
			VariableChangeNotifier notifier,
			FakeTimeProvider timeProvider,
			VariableEntity? entity)
		{
			Host = host;
			Session = session;
			Provider = provider;
			Registry = registry;
			Notifier = notifier;
			TimeProvider = timeProvider;
			_entity = entity;
		}

		private readonly VariableEntity? _entity;

		public UiTestHost Host { get; }

		public SliderWidgetSession Session { get; }

		public RecordingVariableProvider Provider { get; }

		public VariableRegistry Registry { get; }

		public VariableChangeNotifier Notifier { get; }

		public FakeTimeProvider TimeProvider { get; }

		public VariableEntity Entity => _entity!;

		public string Value => Registry.FindByName(VariableScope.Global, null, VariableName)!.Value;

		/// <summary>What a provider reporting a fresh reading looks like from the session's side: the
		/// registry entry moves and the change is published.</summary>
		public void Report(string value)
		{
			Registry.FindByName(VariableScope.Global, null, VariableName)!.Value = value;
			Notifier.Publish(VariableName);
		}

		public async Task DisposeAsync() => await Session.DisposeAsync();

		public static SliderUnderTest Build(
			Action<VariableEntity>? entity,
			double configMin,
			double configMax,
			double configStep,
			bool locked = false,
			string? bindingName = null)
		{
			var registry = new VariableRegistry();
			var provider = new RecordingVariableProvider(registry);

			VariableEntity? stored = null;
			if (entity is not null)
			{
				stored = new VariableEntity
				{
					Id = Guid.NewGuid(),
					Name = VariableName,
					Scope = VariableScope.Global,
					Type = DomainVariableType.Numeric,
					Classification = VariableClassification.User,
					Value = string.Empty,
					UpdatedAt = DateTime.UtcNow
				};
				entity(stored);
				registry.Upsert(stored);
			}

			var integrations = new ConfigurableIntegrationRegistry([provider]);
			var providers = new VariableCatalogProviders(integrations);
			var scopeFactory = new ServiceCollection()
				.AddSingleton(registry)
				.AddSingleton(providers)
				.AddSingleton<IUserVariableStore>(new NullUserVariableStore())
				.AddSingleton<Mediator.IMediator>(new RecordingMediator())
				.AddSingleton<IVariableRefreshSignal>(new VariableRefreshSignal())
				.AddSingleton<IMusicPlayerPollNudge>(new MusicPlayerPollNudge(integrations))
				.AddScoped<IVariableService, VariableService>()
				.BuildServiceProvider()
				.GetRequiredService<IServiceScopeFactory>();

			var notifier = new VariableChangeNotifier();
			var timeProvider = new FakeTimeProvider();
			var boundName = bindingName ?? VariableName;
			var binding = new SliderVariableBinding(boundName,
				configMin,
				configMax,
				configStep,
				registry,
				notifier,
				scopeFactory);
			var state = new UiState<SliderWidgetReadout>(SliderWidgetReadout.Empty);

			var session = new SliderWidgetSession(state,
				new FakeHostLockState { IsLocked = locked },
				timeProvider,
				isWidgetSurface: true,
				binding);

			var config = new SliderWidgetData
			{
				ValueVariable = boundName,
				ShowValue = true,
				Min = configMin,
				Max = configMax,
				Step = configStep
			};
			var element = SliderWidgetView.Build(config, state, icon: null, session.BuildEvents());
			var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				element);

			session.Attach(view);

			return new SliderUnderTest(UiTestHost.Render(view),
				session,
				provider,
				registry,
				notifier,
				timeProvider,
				stored);
		}
	}

	/// <summary>The owning integration of an integration-classified variable, recording what
	/// <see cref="IVariableService.SetValue" /> dispatched to it and applying it to the registry the way a
	/// real provider's next reading would.</summary>
	private sealed class RecordingVariableProvider : IIntegration, IVariableProvider
	{
		internal const string IntegrationId = "integration";

		private readonly VariableRegistry _registry;
		private readonly List<double> _writes = [];

		public RecordingVariableProvider(VariableRegistry registry)
		{
			_registry = registry;
		}

		public string Id => IntegrationId;

		public LocalizedText Name => IntegrationId;

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<VariableDefinition> Variables { get; } =
			[VariableDefinition.Eager("vol", SdkVariableType.Numeric)];

		public List<double> Writes
		{
			get
			{
				lock (_writes)
				{
					return [.. _writes];
				}
			}
		}

		public double? LastWrite
		{
			get
			{
				lock (_writes)
				{
					return _writes.Count == 0 ? null : _writes[^1];
				}
			}
		}

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);

		public ValueTask<VariableWriteResult> SetValueAsync(
			string localId,
			object? value,
			CancellationToken cancellationToken = default)
		{
			if (value is double number)
			{
				lock (_writes)
				{
					_writes.Add(number);
				}
			}

			return ValueTask.FromResult(VariableWriteResult.Applied());
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
