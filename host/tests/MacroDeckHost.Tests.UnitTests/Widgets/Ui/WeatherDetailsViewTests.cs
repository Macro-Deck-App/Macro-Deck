using System.Globalization;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Services;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Widgets.Weather;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class WeatherDetailsViewTests
{
	/// <summary>
	/// The dialog's whole point is the detail a station carries, so it has to survive being handed one.
	/// It did not: the hourly repeat keyed each row on the payload's own <c>HH:mm</c>, and a node id has
	/// no colon in its grammar, so materializing the tree threw and the session published nothing at all -
	/// the modal opened onto an empty surface with no error anywhere. Asserting the rows render is what
	/// makes that a failure rather than an absence.
	/// </summary>
	[Test]
	public void The_dialog_renders_the_hours_and_days_a_station_carries()
	{
		var host = Render(Sample());

		var ids = Walk(host.Root).Select(node => node.Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Has.Some.Contains("14-00"), "The 14:00 hour did not render.");
			Assert.That(ids, Has.Some.Contains("15-00"), "The 15:00 hour did not render.");
			Assert.That(ids, Has.Some.Contains("2026-07-20"), "The first forecast day did not render.");
		});
	}

	/// <summary>Every id the dialog composes has to satisfy the node id grammar, whatever a station's own
	/// values look like - the hour key was only the first value that did not.</summary>
	[Test]
	public void Every_composed_node_id_is_a_valid_identifier()
	{
		var host = Render(Sample());

		Assert.Multiple(() =>
		{
			foreach (var id in Walk(host.Root).Select(node => node.Id))
			{
				Assert.That(id,
					Does.Match("^[A-Za-z0-9][A-Za-z0-9.\\-_]*$"),
					$"'{id}' is not a valid node id.");
			}
		});
	}

	/// <summary>
	/// A text has no intrinsic extent along a horizontal stack's own axis - the host tree carries no font
	/// metrics, so the renderer counts it as zero. A filling sibling is therefore handed the width its
	/// text neighbours actually need on top of its own, and they are pushed out of the box. The forecast
	/// row did exactly that and lost its high temperature off the right edge, so every text sharing a row
	/// with a filling sibling has to declare its own main size.
	/// </summary>
	[Test]
	public void No_text_shares_a_row_with_a_filling_sibling_without_declaring_its_width()
	{
		var host = Render(Sample());

		var rows = Walk(host.Root)
			.Where(node => node.Type == UiComponents.Stack &&
				node.Text(UiComponentProperties.Direction) == UiComponentDirections.Horizontal &&
				node.Children.Any(child => child.Flag(UiComponentProperties.Fill) == true))
			.ToList();

		Assert.That(rows, Is.Not.Empty, "No row mixes a filling child with siblings, so nothing was checked.");
		Assert.Multiple(() =>
		{
			foreach (var child in rows.SelectMany(row => row.Children))
			{
				if (child.Type != UiComponents.Text || child.Flag(UiComponentProperties.Fill) == true)
				{
					continue;
				}

				Assert.That(child.HasProperty(UiComponentProperties.MainSize),
					Is.True,
					$"'{child.Id}' shares a row with a filling sibling but declares no main size, " +
					"so the filling sibling is handed its width too.");
			}
		});
	}

	[Test]
	public void Sunrise_and_sunset_sit_in_their_own_row_below_the_other_measurements()
	{
		var host = Render(Sample());

		UiTestNode Measurement(string key) => Walk(host.Root).Single(node =>
			node.Id.EndsWith($".{key}", StringComparison.Ordinal) &&
			node.Parent?.Parent?.Id.EndsWith(".measurements", StringComparison.Ordinal) == true);

		var wind = Measurement("wind");
		var sunrise = Measurement("sunrise");
		var sunset = Measurement("sunset");

		Assert.Multiple(() =>
		{
			Assert.That(sunrise.Parent!.Id, Is.Not.EqualTo(wind.Parent!.Id));
			Assert.That(sunset.Parent!.Id, Is.EqualTo(sunrise.Parent.Id));
			Assert.That(Measurement("humidity").Parent!.Id, Is.EqualTo(wind.Parent.Id));
			Assert.That(Measurement("precipitation").Parent!.Id, Is.EqualTo(wind.Parent.Id));
		});
	}

	[Test]
	public void The_hourly_strip_scrolls_sideways_through_every_hour_and_falls_back_to_eight_columns()
	{
		var strip = HourlyStrip(Render(WithHours(10)));

		Assert.Multiple(() =>
		{
			Assert.That(strip.Type, Is.EqualTo(UiComponents.List));
			Assert.That(strip.Text(UiComponentProperties.Direction), Is.EqualTo(UiComponentDirections.Horizontal));
			Assert.That(strip.RequiredComponentVersion, Is.EqualTo(2));
			Assert.That(strip.Children, Has.Count.EqualTo(10));

			var fallback = strip.Fallback;
			Assert.That(fallback, Is.Not.Null);
			Assert.That(fallback!.Type, Is.EqualTo(UiComponents.Stack));
			Assert.That(fallback.Text(UiComponentProperties.Direction), Is.EqualTo(UiComponentDirections.Horizontal));
			Assert.That(fallback.RequiredComponentVersion, Is.Null);
			Assert.That(fallback.Children, Has.Count.EqualTo(8));
		});
	}

	[Test]
	public void The_hourly_strip_does_not_negotiate_for_a_reader_that_only_knows_list_version_1()
	{
		var strip = HourlyStrip(Render(WithHours(10)));
		var node = new UiNode
			{ Id = strip.Id, Type = strip.Type, RequiredComponentVersion = strip.RequiredComponentVersion };

		UiNegotiationResult Negotiate(int maximum) => UiCapabilityNegotiator.NegotiateComponent(node,
			new UiCapabilities
			{
				UiProtocol = new UiVersionRange { Minimum = 1, Maximum = 1 },
				SupportsAllComponents = false,
				Components = new Dictionary<string, UiVersionRange>
				{
					[UiComponents.List] = new() { Minimum = 1, Maximum = maximum },
					[UiComponents.Stack] = new() { Minimum = 1, Maximum = 1 },
				},
			});

		Assert.Multiple(() =>
		{
			Assert.That(Negotiate(1).IsSupported, Is.False, "A version 1 reader would draw the strip as a column.");
			Assert.That(Negotiate(2).IsSupported, Is.True);
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(
					new UiNode { Id = strip.Fallback!.Id, Type = strip.Fallback.Type },
					new UiCapabilities
					{
						UiProtocol = new UiVersionRange { Minimum = 1, Maximum = 1 },
						SupportsAllComponents = false,
						Components = new Dictionary<string, UiVersionRange>
						{
							[UiComponents.Stack] = new() { Minimum = 1, Maximum = 1 },
						},
					}).IsSupported,
				Is.True);
		});
	}

	private static UiTestNode HourlyStrip(UiTestHost host)
		=> Walk(host.Root).Single(node => node.Id.EndsWith(".hourly.hours", StringComparison.Ordinal));

	private static WeatherStatePayload WithHours(int count)
	{
		var payload = Sample();
		payload.Hours = Enumerable.Range(8, count)
			.Select(hour => new WeatherHourPayload { Time = $"{hour:00}:00", Condition = "rain", Temperature = 20 })
			.ToList();
		return payload;
	}

	private static IEnumerable<UiTestNode> Walk(UiTestNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}
	}

	[Test]
	public void The_hours_follow_the_app_time_format()
	{
		var culture = CultureInfo.GetCultureInfo("en-US");
		var twelveHour = Render(Sample(), new TimeOfDayFormat(culture, HourCycles.H12).Format);
		var twentyFourHour = Render(Sample(), new TimeOfDayFormat(culture, HourCycles.H23).Format);
		var expected = new TimeOfDayFormat(culture, HourCycles.H12).Format(new TimeOnly(14, 0));

		Assert.Multiple(() =>
		{
			Assert.That(expected, Does.Match(@"^2:00\sPM$"));
			Assert.That(twelveHour.ByText(expected), Is.Not.Empty, "The 14:00 hour is not shown as 12-hour.");
			Assert.That(twelveHour.ByText("14:00"), Is.Empty);
			Assert.That(twentyFourHour.ByText("14:00"), Is.Not.Empty, "The 14:00 hour is not shown as 24-hour.");
		});
	}

	private static UiTestHost Render(WeatherStatePayload payload, Func<TimeOnly, string>? formatTime = null)
	{
		var state = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(payload), payload);
		var icons = WeatherWidgetIcons.EnsureRegistered(new UiResourceStore());

		return UiTestHost.Render(WeatherDetailsView.Build(state, icons, formatTime),
			new UiSurface { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive });
	}

	private static WeatherStatePayload Sample() => new()
	{
		IsAvailable = true,
		StationExists = true,
		InstanceId = "app.macro-deck.weather::loc1",
		LocationName = "Niederkassel, Germany",
		Temperature = 23,
		Condition = "partly-cloudy",
		IsDay = true,
		Unit = "celsius",
		Days =
		[
			new WeatherForecastDayPayload { Date = "2026-07-20", Condition = "partly-cloudy", Min = 12, Max = 24 },
			new WeatherForecastDayPayload { Date = "2026-07-21", Condition = "rain", Min = 10, Max = 19 },
		],
		Hours =
		[
			new WeatherHourPayload { Time = "14:00", Condition = "partly-cloudy", Temperature = 23 },
			new WeatherHourPayload { Time = "15:00", Condition = "rain", Temperature = 22 },
			new WeatherHourPayload { Time = "16:00", Condition = "rain", Temperature = 21 },
		],
	};
}
