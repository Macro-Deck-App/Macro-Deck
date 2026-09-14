using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A27 - <see cref="FakeScreenSaverProviderContext" /> enforces the same identity and validation rules the
/// host does, so a plugin author's green test against it is not lying about what a real host would do.
/// </summary>
[TestFixture]
public class A27_ScreenSaverProviderFakeTests
{
	private static ScreenSaverDescriptor ScreenSaver(string id, bool interactive = false)
		=> new(id, LocalizedText.FromLiteral("Clock"), Interactive: interactive);

	[Test]
	public async Task Registering_the_same_local_id_again_replaces_rather_than_duplicates()
	{
		var context = new FakeScreenSaverProviderContext();

		await context.RegisterScreenSaverAsync(ScreenSaver("clock"));
		await context.RegisterScreenSaverAsync(ScreenSaver("clock", interactive: true));

		Assert.Multiple(() =>
		{
			Assert.That(context.ScreenSavers, Has.Count.EqualTo(1));
			Assert.That(context.ScreenSavers["clock"].Interactive, Is.True);
		});
	}

	[Test]
	public async Task Unregistering_an_unknown_id_is_a_silent_no_op()
	{
		var context = new FakeScreenSaverProviderContext();
		await context.RegisterScreenSaverAsync(ScreenSaver("clock"));

		Assert.DoesNotThrowAsync(() => context.UnregisterScreenSaverAsync("never-registered"));
		Assert.That(context.ScreenSavers, Has.Count.EqualTo(1));
	}

	[Test]
	public void An_empty_id_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeScreenSaverProviderContext();

		Assert.That(() => context.RegisterScreenSaverAsync(ScreenSaver(string.Empty)), Throws.ArgumentException);
	}

	[Test]
	public void An_empty_name_is_rejected_the_way_the_host_rejects_it()
	{
		var context = new FakeScreenSaverProviderContext();

		Assert.That(() => context.RegisterScreenSaverAsync(new ScreenSaverDescriptor("clock", default)),
			Throws.ArgumentException);
	}

	[Test]
	public async Task Every_call_is_recorded_in_order()
	{
		var context = new FakeScreenSaverProviderContext();

		await context.RegisterScreenSaverAsync(ScreenSaver("clock"));
		await context.UnregisterScreenSaverAsync("clock");

		Assert.That(context.Calls.Select(call => call.Kind),
			Is.EqualTo(new[] { ScreenSaverProviderCallKind.Register, ScreenSaverProviderCallKind.Unregister }));
	}
}
