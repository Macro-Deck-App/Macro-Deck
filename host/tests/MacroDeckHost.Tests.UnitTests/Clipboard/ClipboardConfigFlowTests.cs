using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Clipboard.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

[TestFixture]
internal sealed class ClipboardConfigFlowTests
{
	[Test]
	public async Task The_text_variable_cannot_be_turned_on_without_watching()
	{
		var flow = new ClipboardConfigFlow(_ => Task.FromResult(ClipboardSettings.Off));

		var result = await flow.SubmitAsync("settings",
			new Dictionary<string, object?>
			{
				[ClipboardConfigKeys.Name] = "Clipboard",
				[ClipboardConfigKeys.Watch] = false,
				[ClipboardConfigKeys.ExposeText] = true
			},
			new ClipboardFlowContext(null),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values![ClipboardConfigKeys.Watch].Value, Is.EqualTo("false"));
			Assert.That(result.Values[ClipboardConfigKeys.ExposeText].Value, Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task Editing_the_entry_keeps_its_title_and_shows_the_saved_toggles()
	{
		var flow = new ClipboardConfigFlow(_ => Task.FromResult(new ClipboardSettings(true, true)));
		var context = new ClipboardFlowContext("My clipboard");

		var step = (await flow.StartAsync(context, CancellationToken.None)).NextStep!;
		var result = await flow.SubmitAsync("settings",
			new Dictionary<string, object?>
			{
				[ClipboardConfigKeys.Watch] = "true",
				[ClipboardConfigKeys.ExposeText] = "true"
			},
			context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(step.Fields.Select(field => field.Name),
				Is.EqualTo(new[] { ClipboardConfigKeys.Watch, ClipboardConfigKeys.ExposeText }));
			Assert.That(step.Fields.Select(field => field.DefaultValue), Is.All.EqualTo(true));
			Assert.That(result.EntryTitle, Is.EqualTo("My clipboard"));
			Assert.That(result.Values![ClipboardConfigKeys.ExposeText].Value, Is.EqualTo("true"));
		});
	}

	[Test]
	public async Task A_new_entry_needs_a_name()
	{
		var flow = new ClipboardConfigFlow(_ => Task.FromResult(ClipboardSettings.Off));

		var result = await flow.SubmitAsync("settings",
			new Dictionary<string, object?> { [ClipboardConfigKeys.Watch] = true },
			new ClipboardFlowContext(null),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { ClipboardConfigKeys.Name }));
		});
	}
}
