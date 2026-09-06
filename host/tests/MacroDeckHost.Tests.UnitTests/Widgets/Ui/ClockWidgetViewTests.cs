using System.Reflection;
using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Widgets.Clock;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class ClockWidgetViewTests
{
	private static readonly string[] _dialPropertyKeys =
		["mainSize", "fill", "value", "seconds", "color", "events"];

	[Test]
	public void The_tree_does_not_depend_on_when_it_was_built()
	{
		var first = Render(new { });

		Thread.Sleep(TimeSpan.FromMilliseconds(1200));

		var second = Render(new { });

		Assert.Multiple(() =>
		{
			Assert.That(second.ToCanonicalJson(),
				Is.EqualTo(first.ToCanonicalJson()),
				"A clock tree that changes with the build time is carrying an instant instead of a reference.");
			Assert.That(first.Patches, Is.Empty);
			Assert.That(first.Revision, Is.Zero);
		});
	}

	[Test]
	public void The_time_and_the_date_travel_as_references_rather_than_text()
	{
		var host = Render(new { });

		Assert.Multiple(() =>
		{
			var time = Single(host, UiTimeFormats.Time);
			var date = Single(host, UiTimeFormats.Date);

			Assert.That(time.Property("value")!.Value.GetRawText(), Is.EqualTo("""{"$time":{}}"""));
			Assert.That(date.Property("value")!.Value.GetRawText(), Is.EqualTo("""{"$time":{}}"""));

			foreach (var node in Walk(host.Root))
			{
				var text = node.Text("text");

				Assert.That(text ?? string.Empty,
					Does.Not.Match(@"\d{1,2}[:.]\d{2}"),
					$"'{node.Id}' carries a host-formatted time.");
			}
		});
	}

	[Test]
	public void A_zone_is_carried_when_configured_and_omitted_otherwise()
	{
		var zoned = Single(Render(new { timeZone = "America/New_York" }), UiTimeFormats.Time);
		var blank = Single(Render(new { timeZone = "   " }), UiTimeFormats.Time);

		Assert.Multiple(() =>
		{
			Assert.That(zoned.Property("value")!.Value.GetRawText(),
				Is.EqualTo("""{"$time":{"zone":"America/New_York"}}"""));
			Assert.That(blank.Property("value")!.Value.GetRawText(),
				Is.EqualTo("""{"$time":{}}"""),
				"An absent zone is omitted, never written as an explicit null.");
		});
	}

	[Test]
	public void The_view_takes_no_size_argument_and_every_length_is_a_basis_fraction()
	{
		var buildMethod = typeof(ClockWidgetView).GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		// Narrowed rather than dropped when the corner radius was admitted (ADR 0064): the property this
		// protects is that a resize never requires a new tree, and a radius does not move when a widget
		// is resized - a four-cell tile carries the same corner as a one-cell one. A width, a height or
		// a basis still may not reach a view.
		Assert.That(buildMethod.GetParameters()
				.Where(p => p.ParameterType == typeof(int) || p.ParameterType == typeof(double))
				.Select(p => p.Name),
			Is.SubsetOf(new[] { "cornerRadius" }),
			"The view must not take a width/height/basis argument - a resize must never require a new tree.");

		var host = Render(new { style = "analog" });
		var lengthKeys = new[] { "mainSize", "size", "minSize", "gap", "padding" };

		Assert.Multiple(() =>
		{
			foreach (var node in Walk(host.Root))
			{
				foreach (var key in lengthKeys)
				{
					if (node.Property(key) is not { } value)
					{
						continue;
					}

					Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Object));
					Assert.That(value.TryGetProperty("basis", out var basis), Is.True);
					Assert.That(basis.GetDouble(), Is.LessThan(3), $"'{node.Id}'.{key} looks like a pixel value.");
				}
			}

			Assert.That(host.Tree.Surface.Attributes.Keys,
				Has.None.Contains("gridSpan").And.None.Contains("width").And.None.Contains("height"));
		});
	}

	[Test]
	public void The_dial_carries_no_resolved_geometry()
	{
		var dial = Render(new { style = "analog" }).SingleByType(UiMacroDeckComponents.ClockDial);

		Assert.That(dial.PropertyKeys,
			Is.SubsetOf(_dialPropertyKeys),
			"Resolving the dial's normative geometry host side would make a resize a round trip.");
	}

	[Test]
	public void The_digital_time_is_one_run()
	{
		var host = Render(new { });

		Assert.That(Rendered(host)
				.Count(node => node.Type == UiMacroDeckComponents.DynamicText &&
					node.Text("format") == UiTimeFormats.Time),
			Is.EqualTo(1),
			"Composing a time out of several runs breaks wherever a language orders them differently.");
	}

	[Test]
	public void An_empty_configuration_renders_todays_defaults()
	{
		var host = Render(new { });

		Assert.Multiple(() =>
		{
			Assert.That(Single(host, UiTimeFormats.Time).Flag("seconds"), Is.True);
			Assert.That(Find(host, UiTimeFormats.Date), Is.Not.Null);
			Assert.That(Find(host, UiTimeFormats.ZoneName), Is.Null);
			Assert.That(Rendered(host).Where(node => node.Type == UiMacroDeckComponents.ClockDial), Is.Empty);
		});
	}

	[Test]
	public void Each_switch_removes_its_node_rather_than_emptying_it()
	{
		var withoutDate = Render(new { showDate = false });
		var withoutSeconds = Render(new { showSeconds = false });
		var analog = Render(new { style = "analog", showSeconds = false });

		Assert.Multiple(() =>
		{
			Assert.That(Find(withoutDate, UiTimeFormats.Date), Is.Null);
			Assert.That(Single(withoutSeconds, UiTimeFormats.Time).Flag("seconds"), Is.Not.True);
			Assert.That(analog.SingleByType(UiMacroDeckComponents.ClockDial).Flag("seconds"), Is.Not.True);
			Assert.That(Find(analog, UiTimeFormats.Time), Is.Null);
			Assert.That(Find(analog, UiTimeFormats.Date), Is.Not.Null);
		});
	}

	[Test]
	public void The_caption_is_a_reference_unless_the_user_typed_one()
	{
		var zoned = Render(new { timeZone = "America/New_York" });
		var labelled = Render(new { timeZone = "America/New_York", label = "  Berlin Office  " });
		var bare = Render(new { });

		Assert.Multiple(() =>
		{
			Assert.That(Find(zoned, UiTimeFormats.ZoneName),
				Is.Not.Null,
				"A zone caption is derived by the reader, never spelled out by the host.");
			Assert.That(labelled.ById("clock.caption").Text("text"), Is.EqualTo("Berlin Office"));
			Assert.That(labelled.ById("clock.caption").Type, Is.EqualTo(UiComponents.Text));
			Assert.That(bare.FindById("clock.caption"), Is.Null);
		});
	}

	[Test]
	public void The_removed_time_source_option_no_longer_reaches_the_tree()
	{
		var absent = Render(new { }).ToCanonicalJson();

		Assert.Multiple(() =>
		{
			Assert.That(Render(new { timeSource = "device" }).ToCanonicalJson(), Is.EqualTo(absent));
			Assert.That(Render(new { timeSource = "host" }).ToCanonicalJson(), Is.EqualTo(absent));
		});
	}

	[Test]
	public void Every_new_node_type_degrades_to_a_type_an_older_reader_knows()
	{
		var frozen = new[]
		{
			UiComponents.Stack, UiComponents.Text, UiComponents.Image, UiComponents.RangeBar,
		};

		Assert.Multiple(() =>
		{
			foreach (var host in new[]
				{
					Render(new { }), Render(new { style = "analog", timeZone = "Europe/Berlin" }),
					Render(new
					{
						hourCycle = "12h", leadingZero = false, dateFormat = "iso",
						timeZone = "Europe/Berlin", showOffset = true,
					}),
				})
			{
				foreach (var node in Walk(host.Root).Where(node => !frozen.Contains(node.Type, StringComparer.Ordinal)))
				{
					var terminal = node;

					while (terminal.Fallback is { } next)
					{
						terminal = next;
					}

					Assert.That(terminal.Type,
						Is.AnyOf(frozen),
						$"'{node.Id}' leaves an older reader with nothing but the unsupported placeholder.");
				}
			}
		});
	}

	[TestCase("auto", true, "time")]
	[TestCase("auto", false, "time")]
	[TestCase("12h", true, "time-12h-padded")]
	[TestCase("12h", false, "time-12h")]
	[TestCase("24h", true, "time-24h")]
	[TestCase("24h", false, "time-24h-unpadded")]
	public void The_chosen_face_and_leading_zero_name_the_format(string cycle, bool leadingZero, string format)
	{
		var host = Render(new { hourCycle = cycle, leadingZero });

		Assert.That(Single(host, format), Is.Not.Null);
	}

	[TestCase("default", "date")]
	[TestCase("day-first", "date-day-first")]
	[TestCase("month-first", "date-month-first")]
	[TestCase("iso", "date-iso")]
	[TestCase("long", "date-long")]
	public void The_chosen_date_format_names_the_format(string choice, string format)
	{
		var host = Render(new { dateFormat = choice });

		Assert.That(Single(host, format), Is.Not.Null);
	}

	[Test]
	public void A_format_the_readers_language_did_not_choose_is_negotiated_and_falls_back_to_one_it_did()
	{
		// The hazard this exists for: an unknown *format* is caught by nothing, so a reader that predates
		// one draws an empty run where the clock belongs. Asking for a version it cannot answer is what
		// turns that blank into the run carried below it.
		var pinned = Render(new { hourCycle = "12h", dateFormat = "iso" });
		var time = Single(pinned, UiTimeFormats.Time12HourPadded);
		var date = Single(pinned, UiTimeFormats.DateIso);

		Assert.Multiple(() =>
		{
			Assert.That(time.RequiredComponentVersion, Is.EqualTo(2));
			Assert.That(date.RequiredComponentVersion, Is.EqualTo(2));
			Assert.That(time.Fallback?.Text("format"), Is.EqualTo(UiTimeFormats.Time));
			Assert.That(date.Fallback?.Text("format"), Is.EqualTo(UiTimeFormats.Date));
			Assert.That(time.Fallback?.RequiredComponentVersion, Is.Null);
			Assert.That(date.Fallback?.RequiredComponentVersion, Is.Null);
		});
	}

	[Test]
	public void A_clock_the_reader_can_already_draw_asks_for_no_version_at_all()
	{
		var host = Render(new { });

		Assert.That(Walk(host.Root).Select(node => node.RequiredComponentVersion),
			Has.All.Null,
			"A default clock negotiates nothing: every reader that knows the type can draw it.");
	}

	[Test]
	public void The_chosen_colours_reach_every_line_and_the_dial()
	{
		var digital = Render(new
		{
			backgroundColor = "#101c2c", textColor = "#7fd4ff", timeZone = "Europe/Berlin",
			showOffset = true,
		});
		var analog = Render(new { style = "analog", textColor = "#7fd4ff" });

		Assert.Multiple(() =>
		{
			Assert.That(digital.Root.Text("background"), Is.EqualTo("#101c2c"));

			foreach (var node in Rendered(digital)
				.Where(node => node.Type == UiMacroDeckComponents.DynamicText))
			{
				Assert.That(node.Text("color"), Is.EqualTo("#7fd4ff"), $"'{node.Id}' is not tinted.");
			}

			Assert.That(analog.SingleByType(UiMacroDeckComponents.ClockDial).Text("color"),
				Is.EqualTo("#7fd4ff"));
		});
	}

	[Test]
	public void An_unset_colour_leaves_the_property_off_rather_than_writing_a_null()
	{
		var host = Render(new { style = "analog", showDate = true });

		Assert.Multiple(() =>
		{
			foreach (var node in Walk(host.Root))
			{
				Assert.That(node.PropertyKeys,
					Has.None.EqualTo("color").And.None.EqualTo("background"),
					$"'{node.Id}' carries a colour nobody chose.");
			}
		});
	}

	[TestCase("below", "time", "date")]
	[TestCase("above", "date", "time")]
	public void A_date_above_or_below_stays_on_the_clocks_own_column(string position, string first, string second)
	{
		var host = Render(new { datePosition = position });

		Assert.Multiple(() =>
		{
			Assert.That(host.Root.Children.Select(node => node.Text("format")),
				Is.EqualTo(new[] { first, second }).AsCollection);
			Assert.That(host.Root.Text("direction"), Is.EqualTo(UiComponentDirections.Vertical));
		});
	}

	[TestCase("left", "date", "time")]
	[TestCase("right", "time", "date")]
	public void A_date_beside_the_time_puts_the_two_on_one_row(string position, string first, string second)
	{
		var host = Render(new { datePosition = position });
		var row = host.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(row.Type, Is.EqualTo(UiComponents.Stack));
			Assert.That(row.Text("direction"), Is.EqualTo(UiComponentDirections.Horizontal));
			Assert.That(row.Children.Select(node => node.Text("format")),
				Is.EqualTo(new[] { first, second }).AsCollection);
		});
	}

	[Test]
	public void A_position_changes_nothing_when_there_is_no_date_to_place()
	{
		var below = Render(new { showDate = false, datePosition = "below" }).ToCanonicalJson();

		Assert.That(Render(new { showDate = false, datePosition = "left" }).ToCanonicalJson(),
			Is.EqualTo(below));
	}

	[Test]
	public void The_offset_line_is_drawn_only_once_a_zone_is_chosen_to_be_offset_from()
	{
		var zoned = Render(new { timeZone = "Europe/Berlin", showOffset = true });
		var deviceZone = Render(new { showOffset = true });
		var off = Render(new { timeZone = "Europe/Berlin" });

		Assert.Multiple(() =>
		{
			Assert.That(Single(zoned, UiTimeFormats.ZoneOffset), Is.Not.Null);
			Assert.That(Find(deviceZone, UiTimeFormats.ZoneOffset),
				Is.Null,
				"The device's own zone has no offset to state, so the line is left out rather than drawn empty.");
			Assert.That(Find(off, UiTimeFormats.ZoneOffset), Is.Null);
		});
	}

	// Rendered nodes only: a fallback subtree is what an older reader draws instead, so counting it here
	// would report a face the configured style deliberately does not show.
	private static UiTestNode? Find(UiTestHost host, string format)
		=> Rendered(host).SingleOrDefault(node => node.Type == UiMacroDeckComponents.DynamicText &&
			node.Text("format") == format);

	private static UiTestNode Single(UiTestHost host, string format)
		=> Rendered(host).Single(node => node.Type == UiMacroDeckComponents.DynamicText &&
			node.Text("format") == format);

	private static IEnumerable<UiTestNode> Rendered(UiTestHost host) => WalkRendered(host.Root);

	private static IEnumerable<UiTestNode> WalkRendered(UiTestNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in WalkRendered(child))
			{
				yield return descendant;
			}
		}
	}

	private static UiTestHost Render(object data)
	{
		var config = ClockWidgetData.Parse(JsonSerializer.SerializeToElement(data));

		return UiTestHost.Render(ClockWidgetView.Build(config),
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });
	}

	private static IEnumerable<UiTestNode> Walk(UiTestNode node)
	{
		yield return node;

		if (node.Fallback is { } fallback)
		{
			foreach (var descendant in Walk(fallback))
			{
				yield return descendant;
			}
		}

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}
	}
}
