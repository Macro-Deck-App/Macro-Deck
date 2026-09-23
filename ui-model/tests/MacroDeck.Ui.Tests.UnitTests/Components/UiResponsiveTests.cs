using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

[TestFixture]
public class UiResponsiveTests
{
	private static readonly string[] _drawnLayouts = ["root.weather.compact", "root.weather.wide", "root.weather.tall"];
	private static readonly string[] _drawnAndCopy = ["root.weather.temp", "root.weather._fallback.temp"];
	private static readonly string[] _variantOnly = ["root.weather.caption"];

	private static UiSurface WidgetSurface()
		=> new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	private static UiTree Build(params UiElement[] children)
		=> UiViewBuilder.Build(WidgetSurface(), new UiStack { Key = "root", Children = children });

	private static UiView View(params UiElement[] children)
		=> new(WidgetSurface(), new UiStack { Key = "root", Children = children });

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var descendant in node.Children.SelectMany(Walk))
		{
			yield return descendant;
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}

	private static UiResponsive Weather(UiElement? fallback = null) => new()
	{
		Key = "weather",
		Fill = true,
		Fallback = fallback,
		Default = new UiStack { Key = "compact", Children = [new UiTextRun { Key = "temp", Text = "21°" }] },
		Variants =
		[
			new UiResponsiveVariant
			{
				MinWidth = 1.5,
				Content = new UiStack { Key = "wide", Direction = UiComponentDirections.Horizontal },
			},
			new UiResponsiveVariant { MaxAspect = 0.67, Content = new UiStack { Key = "tall" } },
		],
	};

	private static int Select(string variants, int childCount, double? width, double? height)
	{
		using var document = JsonDocument.Parse(variants);
		return UiResponsiveSelection.SelectChild(document.RootElement, childCount, width, height);
	}

	[Test]
	public void The_node_carries_the_default_first_then_each_variant_with_its_conditions_index_aligned()
	{
		var responsive = Build(Weather()).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(responsive.Id, Is.EqualTo("root.weather"));
			Assert.That(responsive.Type, Is.EqualTo("ui.responsive"));
			Assert.That(responsive.Children.Select(child => child.Id),
				Is.EqualTo(_drawnLayouts));
			Assert.That(responsive.Properties["variants"].GetRawText(),
				Is.EqualTo("""[{"minWidth":1.5},{"maxAspect":0.67}]"""));
			Assert.That(responsive.Properties["fill"].GetBoolean(), Is.True);
		});
	}

	[Test]
	public void Without_an_explicit_fallback_the_default_is_copied_as_the_fallback_under_its_own_ids()
	{
		var responsive = Build(Weather()).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(responsive.Fallback!.Id, Is.EqualTo("root.weather._fallback.compact"));
			Assert.That(responsive.Fallback.Children.Single().Id, Is.EqualTo("root.weather._fallback.compact.temp"));
			Assert.That(responsive.Fallback.Children.Single().Properties["text"].GetString(), Is.EqualTo("21°"));
		});
	}

	[Test]
	public void A_default_keyed_like_the_node_or_a_sibling_builds_without_colliding_ids()
	{
		var tree = Build(
			new UiTextRun { Key = "compact", Text = "sibling" },
			new UiResponsive { Key = "weather", Default = new UiStack { Key = "weather" } },
			new UiResponsive { Key = "other", Default = new UiStack { Key = "compact" } });

		var ids = Walk(tree.Root).Select(node => node.Id).ToList();

		Assert.That(ids, Is.Unique);
	}

	[Test]
	public void An_explicit_fallback_replaces_the_copy()
	{
		var responsive = Build(Weather(new UiTextRun { Key = "plain", Text = "21°" })).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(responsive.Fallback!.Id, Is.EqualTo("root.plain"));
			Assert.That(Walk(responsive).Any(node => node.Id.Contains("_fallback", StringComparison.Ordinal)), Is.False);
		});
	}

	[Test]
	public void A_reader_without_ui_responsive_draws_the_default_layout_through_the_fallback()
	{
		var responsive = Build(Weather()).Root.Children.Single();
		var older = new UiCapabilities
		{
			UiProtocol = new UiVersionRange { Minimum = 3, Maximum = 4 },
			SupportsAllComponents = false,
			Components = new Dictionary<string, UiVersionRange>
			{
				["ui.stack"] = new() { Minimum = 1, Maximum = 1 },
				["ui.text"] = new() { Minimum = 1, Maximum = 1 },
			},
		};

		Assert.Multiple(() =>
		{
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(responsive, older).IsSupported, Is.False);
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(responsive.Fallback!, older).IsSupported, Is.True);
			Assert.That(responsive.Fallback!.Type, Is.EqualTo("ui.stack"));
		});
	}

	[Test]
	public void A_press_on_the_copy_of_the_default_runs_the_handler_the_default_declared()
	{
		var presses = 0;
		var view = View(new UiResponsive
		{
			Key = "tile",
			Default = new UiButton
			{
				Key = "play",
				Events = [UiEventHandler.On(UiComponentEvents.Press, () => presses++)],
			},
		});

		var drawn = view.Dispatch(new UiEvent { NodeId = "root.tile.play", Name = UiComponentEvents.Press });
		var copy = view.Dispatch(new UiEvent { NodeId = "root.tile._fallback.play", Name = UiComponentEvents.Press });

		Assert.Multiple(() =>
		{
			Assert.That(drawn.IsAccepted, Is.True);
			Assert.That(copy.IsAccepted, Is.True);
			Assert.That(presses, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_state_change_inside_the_default_patches_both_the_drawn_layout_and_its_copy()
	{
		var temperature = new UiState<string>("21°");
		var view = View(new UiResponsive
		{
			Key = "weather",
			Default = new UiTextRun { Key = "temp", Text = UiText.From(() => temperature.Value) },
		});
		view.DrainPatches();

		temperature.Set("22°");

		var changed = view.DrainPatches()
			.SelectMany(patch => patch.Operations)
			.Where(operation => operation.Op == UiPatchOperations.SetProperties)
			.Select(operation => operation.NodeId)
			.ToList();

		Assert.That(changed, Is.EquivalentTo(_drawnAndCopy));
	}

	[Test]
	public void A_state_change_inside_a_variant_patches_that_variant_although_no_reader_is_known_to_show_it()
	{
		var label = new UiState<string>("Sunny");
		var view = View(new UiResponsive
		{
			Key = "weather",
			Default = new UiStack { Key = "compact" },
			Variants =
			[
				new UiResponsiveVariant
				{
					MinWidth = 1.5,
					Content = new UiTextRun { Key = "caption", Text = UiText.From(() => label.Value) },
				},
			],
		});
		view.DrainPatches();

		label.Set("Rain");

		var changed = view.DrainPatches().SelectMany(patch => patch.Operations).Select(op => op.NodeId).ToList();

		Assert.That(changed, Is.EqualTo(_variantOnly));
	}

	[Test]
	public void Layouts_that_cannot_be_drawn_across_the_whole_box_are_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<UiViewException>(() => Build(new UiResponsive
			{
				Key = "r",
				Default = new UiStack { Key = "d" },
				Children = [new UiStack { Key = "c" }],
			}), "children");
			Assert.Throws<UiViewException>(() => Build(new UiResponsive
			{
				Key = "r",
				Default = new UiWhen { Key = "w", Condition = () => true, Content = () => new UiStack { Key = "d" } },
			}), "conditional default");
			Assert.Throws<UiViewException>(() => Build(new UiResponsive
			{
				Key = "r",
				Default = new UiStack { Key = "d" },
				Variants = [new UiResponsiveVariant { MinWidth = 2, Content = new UiStack { Key = "_fallback" } }],
			}), "reserved key");
			Assert.Throws<UiViewException>(() => Build(new UiResponsive
			{
				Key = "r",
				Default = new UiStack { Key = "d", Fill = true },
			}), "slot property on a layout");
			Assert.Throws<UiViewException>(() => Build(new UiResponsive
			{
				Key = "r",
				Default = new UiStack { Key = "d" },
				Variants = [new UiResponsiveVariant { MinWidth = 2, MaxWidth = 2, Content = new UiStack { Key = "v" } }],
			}), "a variant that can never hold");
		});
	}

	[Test]
	public void Negative_widths_and_non_positive_aspects_are_rejected_when_they_are_built()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				_ = new UiResponsiveVariant { MinWidth = -1, Content = new UiStack { Key = "v" } });
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				_ = new UiResponsiveVariant { MaxHeight = double.PositiveInfinity, Content = new UiStack { Key = "v" } });
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				_ = new UiResponsiveVariant { MinAspect = 0, Content = new UiStack { Key = "v" } });
		});
	}

	[Test]
	public void An_input_in_a_default_that_is_copied_is_rejected_naming_the_explicit_fallback_way_out()
	{
		var text = new UiState<string>("");
		UiResponsive Form(UiElement? fallback) => new()
		{
			Key = "form",
			Fallback = fallback,
			Default = new UiStack { Key = "fields", Children = [new UiStringInput { Key = "name", Binding = Bind.To(text) }] },
		};

		var exception = Assert.Throws<UiViewException>(() => Build(Form(null)));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Message, Does.Contain("'form'").And.Contain("explicit Fallback"));
			Assert.DoesNotThrow(() => Build(Form(new UiStack { Key = "plain" })));
		});
	}

	[Test]
	public void An_input_a_conditional_inserts_later_into_the_copied_default_is_rejected_too()
	{
		var text = new UiState<string>("");
		var editing = new UiState<bool>(false);
		var view = View(new UiResponsive
		{
			Key = "form",
			Default = new UiStack
			{
				Key = "fields",
				Children =
				[
					new UiWhen
					{
						Key = "edit",
						Condition = () => editing.Value,
						Content = () => new UiStringInput { Key = "name", Binding = Bind.To(text) },
					},
				],
			},
		});

		var exception = Assert.Catch<Exception>(() => editing.Set(true));

		Assert.That(Unwrap(exception!).Message, Does.Contain("explicit Fallback"));

		GC.KeepAlive(view);
	}

	[Test]
	public void A_copy_whose_ids_would_be_too_long_names_the_node_and_the_explicit_fallback_way_out()
	{
		var longKey = new string('k', 115);

		var exception = Assert.Throws<UiViewException>(() => Build(new UiResponsive
		{
			Key = "tile",
			Default = new UiStack { Key = longKey },
		}));

		Assert.That(exception!.Message, Does.Contain("'tile'").And.Contain("explicit Fallback"));
	}

	[Test]
	public void The_default_is_chosen_when_no_condition_holds_and_otherwise_the_first_that_does()
	{
		const string variants = """[{"minWidth":1.5},{"minWidth":1.5,"minHeight":1.5},{"maxAspect":0.67}]""";

		Assert.Multiple(() =>
		{
			Assert.That(Select(variants, 4, 1, 1), Is.EqualTo(0), "1x1");
			Assert.That(Select(variants, 4, 2.1, 1), Is.EqualTo(1), "2x1");
			Assert.That(Select(variants, 4, 2.1, 2.1), Is.EqualTo(1), "2x2 matches the first that holds");
			Assert.That(Select(variants, 4, 1, 2.1), Is.EqualTo(3), "1x2");
		});
	}

	[Test]
	public void A_minimum_is_inclusive_a_maximum_exclusive_and_both_absorb_rounding_of_a_scaled_box()
	{
		const string atTwo = """[{"minWidth":2}]""";
		const string belowTwo = """[{"maxWidth":2}]""";

		Assert.Multiple(() =>
		{
			Assert.That(Select(atTwo, 2, 2, 1), Is.EqualTo(1));
			Assert.That(Select(atTwo, 2, 239.99999999999997 / 120, 1), Is.EqualTo(1));
			Assert.That(Select(atTwo, 2, 1.99, 1), Is.EqualTo(0));
			Assert.That(Select(belowTwo, 2, 2, 1), Is.EqualTo(0));
			Assert.That(Select(belowTwo, 2, 240.00000000000003 / 120, 1), Is.EqualTo(0));
			Assert.That(Select(belowTwo, 2, 239.99999999999997 / 120, 1), Is.EqualTo(0));
			Assert.That(Select(belowTwo, 2, 1.99, 1), Is.EqualTo(1));
		});
	}

	[Test]
	public void A_bound_on_an_unknown_extent_never_holds_and_a_zero_extent_counts_as_unknown()
	{
		const string variants = """[{"minWidth":1.5},{"maxHeight":1.5},{}]""";

		Assert.Multiple(() =>
		{
			Assert.That(Select(variants, 4, null, 1), Is.EqualTo(2));
			Assert.That(Select(variants, 4, 0, 0), Is.EqualTo(3));
			Assert.That(Select("""[{"minAspect":1.5}]""", 2, 3, null), Is.EqualTo(0));
		});
	}

	[Test]
	public void Malformed_conditions_are_read_leniently()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Select("null", 2, 2, 1), Is.EqualTo(0), "not an array");
			Assert.That(Select("""[42, {}]""", 3, 2, 1), Is.EqualTo(2), "a non-object condition never holds");
			Assert.That(Select("""[{"minWidth":"2"}]""", 2, 1, 1), Is.EqualTo(1), "a non-number member is ignored");
			Assert.That(Select("""[{}, {}]""", 1, 2, 1), Is.EqualTo(0), "a condition without a child is skipped");
			Assert.That(Select("""[{}]""", 0, 2, 1), Is.EqualTo(-1), "no children");
		});
	}

	private static Exception Unwrap(Exception exception)
	{
		while (exception is not UiViewException && exception.InnerException is not null)
		{
			exception = exception.InnerException;
		}

		return exception;
	}
}
