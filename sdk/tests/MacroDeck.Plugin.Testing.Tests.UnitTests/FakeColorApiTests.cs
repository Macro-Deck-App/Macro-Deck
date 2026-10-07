using MacroDeck.Plugin.Testing.Fakes;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class FakeColorApiTests
{
	private static readonly string[] _delivered = ["#3366ff", "#ff0000"];

	[Test]
	public async Task A_watch_gets_its_value_once_then_every_change_until_disposed()
	{
		var colors = new FakeIntegrationContext().Colors;
		var received = new List<string?>();
		await colors.Set("{{ vars.primary | color }}", "#3366ff");

		var watch = await colors.WatchAsync("{{ vars.primary | color }}", (color, _) =>
		{
			received.Add(color);
			return Task.CompletedTask;
		});
		await colors.Set("{{ vars.primary | color }}", "#3366ff");
		await colors.Set("{{ vars.primary | color }}", "#ff0000");
		await watch.DisposeAsync();
		await colors.Set("{{ vars.primary | color }}", "#00ff00");

		Assert.Multiple(async () =>
		{
			Assert.That(received, Is.EqualTo(_delivered));
			Assert.That(await colors.ResolveAsync("{{ vars.other | color }}"), Is.Null);
			Assert.That(await colors.ResolveAsync("#ABC"), Is.EqualTo("#aabbcc"));
		});
	}
}
