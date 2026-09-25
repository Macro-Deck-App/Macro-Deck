using System.Globalization;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiReplaceableWorkTests
{
	private static async Task<List<string>> DrainAsync(UiReplaceableWork<string> work)
	{
		work.Complete();
		var read = new List<string>();

		await foreach (var item in work.ReadAllAsync())
		{
			read.Add(item);
		}

		return read;
	}

	[Test]
	public async Task A_waiting_replaceable_item_is_overwritten_by_the_next_with_its_key()
	{
		var work = new UiReplaceableWork<string>();

		work.WriteReplaceable("a", "a1");
		work.WriteReplaceable("a", "a2");
		work.WriteReplaceable("a", "a3");

		Assert.That(await DrainAsync(work), Is.EqualTo(new[] { "a3" }));
	}

	[Test]
	public async Task Any_other_item_written_after_it_keeps_it_from_being_overwritten()
	{
		var work = new UiReplaceableWork<string>();

		work.WriteReplaceable("a", "a1");
		work.WriteReplaceable("a", "a2");
		work.Write("up");
		work.WriteReplaceable("a", "a3");
		work.WriteReplaceable("a", "a4");

		Assert.That(await DrainAsync(work), Is.EqualTo(new[] { "a2", "up", "a4" }));
	}

	[Test]
	public async Task Interleaved_keys_each_keep_one_waiting_item()
	{
		var work = new UiReplaceableWork<string>();

		for (var step = 0; step < 100; step++)
		{
			work.WriteReplaceable("a", "a" + step);
			work.WriteReplaceable("b", "b" + step);
		}

		Assert.That(await DrainAsync(work), Is.EqualTo(new[] { "a99", "b99" }));
	}

	[Test]
	public async Task An_item_the_reader_has_taken_is_not_overwritten_afterwards()
	{
		var work = new UiReplaceableWork<string>();
		var reader = work.ReadAllAsync().GetAsyncEnumerator();

		work.WriteReplaceable("a", "a1");
		Assert.That(await reader.MoveNextAsync(), Is.True);
		work.WriteReplaceable("a", "a2");
		Assert.That(await reader.MoveNextAsync(), Is.True);

		Assert.That(reader.Current, Is.EqualTo("a2"));
		await reader.DisposeAsync();
	}

	[Test]
	public async Task Concurrent_writers_never_lose_or_reorder_an_ordinary_item()
	{
		var work = new UiReplaceableWork<string>();

		var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(() =>
		{
			for (var step = 0; step < 500; step++)
			{
				work.WriteReplaceable("move" + writer, "move");
				work.Write($"{writer}:{step}");
			}
		}));
		await Task.WhenAll(writers);

		var ordinary = (await DrainAsync(work)).Where(item => item != "move").ToList();

		Assert.That(ordinary, Has.Count.EqualTo(2000));
		for (var writer = 0; writer < 4; writer++)
		{
			var own = ordinary.Where(item => item.StartsWith(writer + ":", StringComparison.Ordinal))
				.Select(item => int.Parse(item[(item.IndexOf(':') + 1)..], CultureInfo.InvariantCulture));
			Assert.That(own, Is.Ordered);
		}
	}
}
