using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Dsl;

[TestFixture]
public class UiPointerEventDataTests
{
	private static UiEventData Received(string name, string json)
	{
		UiEventData? received = null;
		var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			new UiStack
			{
				Key = "root",
				Children = [new UiStack { Key = "pad", Events = [UiEventHandler.On(name, data => received = data)] }],
			});

		using var document = JsonDocument.Parse(json);
		var result = view.Dispatch(new UiEvent
		{
			NodeId = view.Tree.Root.Children.Single().Id, Name = name, Data = document.RootElement.Clone()
		});

		Assert.That(result.IsAccepted, Is.True, result.Reason);

		return received!.Value;
	}

	[Test]
	public void A_pointer_down_reads_its_position_time_and_the_node_size_and_ignores_members_it_does_not_know()
	{
		var data = Received(UiComponentEvents.PointerDown,
			"""{"id":2147483647,"x":0.5,"y":0.25,"t":0,"width":1,"height":0.5,"pressure":0.3}""");

		Assert.That(data.TryGetPointerDown(out var down), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(down.Sample.Id, Is.EqualTo(int.MaxValue));
			Assert.That((down.Sample.X, down.Sample.Y, down.Sample.TimeMs), Is.EqualTo((0.5, 0.25, 0d)));
			Assert.That((down.Width, down.Height), Is.EqualTo((1d, 0.5)));
		});
	}

	[Test]
	public void A_pointer_move_reads_every_sample_oldest_first()
	{
		var data = Received(UiComponentEvents.PointerMove,
			"""{"samples":[{"id":4,"x":0.1,"y":0,"t":16},{"id":9,"x":0.9,"y":0.2,"t":17},{"id":4,"x":0.2,"y":0,"t":31}]}""");

		Assert.That(data.TryGetPointerSamples(out var samples), Is.True);
		Assert.That(samples.Select(sample => (sample.Id, sample.X, sample.TimeMs)),
			Is.EqualTo(new[] { (4, 0.1, 16d), (9, 0.9, 17d), (4, 0.2, 31d) }));
	}

	[Test]
	public void A_pointer_up_tells_a_lifted_finger_from_a_cancelled_one()
	{
		var lifted = Received(UiComponentEvents.PointerUp, """{"id":4,"x":0.2,"y":0,"t":40}""");
		var cancelled = Received(UiComponentEvents.PointerUp, """{"id":4,"x":0.2,"y":0,"t":40,"cancelled":true}""");

		Assert.Multiple(() =>
		{
			Assert.That(lifted.TryGetPointerUp(out var up), Is.True);
			Assert.That(up.Cancelled, Is.False);
			Assert.That(up.Sample.Id, Is.EqualTo(4));
			Assert.That(cancelled.TryGetPointerUp(out var gone), Is.True);
			Assert.That(gone.Cancelled, Is.True);
		});
	}

	[Test]
	public void A_tap_reads_how_many_pointers_took_part()
	{
		Assert.That(Received(UiComponentEvents.Tap, """{"pointers":2}""").TryGetTap(out var pointers), Is.True);
		Assert.That(pointers, Is.EqualTo(2));
	}

	[TestCase(UiComponentEvents.PointerDown, """{"id":1,"x":0,"y":0,"t":0}""")]
	[TestCase(UiComponentEvents.PointerDown, """{"id":2147483648,"x":0,"y":0,"t":0,"width":1,"height":1}""")]
	[TestCase(UiComponentEvents.PointerMove, """{"samples":{"id":1}}""")]
	[TestCase(UiComponentEvents.PointerMove, """{"samples":[{"id":1,"x":0,"y":0,"t":0},{"id":1,"x":"0","y":0,"t":1}]}""")]
	[TestCase(UiComponentEvents.PointerMove, """[{"id":1,"x":0,"y":0,"t":0}]""")]
	[TestCase(UiComponentEvents.PointerUp, """{"id":1.5,"x":0,"y":0,"t":0}""")]
	[TestCase(UiComponentEvents.Tap, """{"pointers":0}""")]
	[TestCase(UiComponentEvents.Tap, "2")]
	public void A_malformed_payload_is_refused_rather_than_half_read(string name, string json)
	{
		var data = Received(name, json);

		var read = name switch
		{
			UiComponentEvents.PointerDown => data.TryGetPointerDown(out _),
			UiComponentEvents.PointerMove => data.TryGetPointerSamples(out _),
			UiComponentEvents.PointerUp => data.TryGetPointerUp(out _),
			_ => data.TryGetTap(out _),
		};

		Assert.That(read, Is.False);
	}
}
