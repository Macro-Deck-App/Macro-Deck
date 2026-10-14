using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Ui;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A35UiEventOriginFakeTests
{
	[Test]
	public async Task It_records_what_a_session_showed_on_its_client()
	{
		var origin = new FakeUiEventOrigin("client-a");

		var reported = await origin.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));
		var opened = await origin.ShowModalAsync(new ModalDefinition { ViewId = "details" });

		Assert.Multiple(() =>
		{
			Assert.That(reported, Is.True);
			Assert.That(opened, Is.True);
			Assert.That(origin.ReportedFailures.Single().Literal, Is.EqualTo("Offline."));
			Assert.That(origin.OpenedModals.Single().ViewId, Is.EqualTo("details"));
		});
	}

	[Test]
	public async Task A_client_that_went_away_is_answered_as_macro_deck_answers_it()
	{
		var origin = new FakeUiEventOrigin("client-a") { IsAttached = false };

		var reported = await origin.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));
		var answer = await origin.ShowModalAsync<string>(new ModalDefinition { ViewId = "details" });

		Assert.Multiple(() =>
		{
			Assert.That(reported, Is.False);
			Assert.That(answer.Cancelled, Is.True);
			Assert.That(origin.ReportedFailures, Is.Empty);
		});
	}

	[Test]
	public async Task A_dialog_answers_with_the_scripted_value()
	{
		var origin = new FakeUiEventOrigin
			{ ModalAnswer = _ => System.Text.Json.JsonSerializer.SerializeToElement("yes") };

		var answer = await origin.ShowModalAsync<string>(new ModalDefinition { ViewId = "confirm" });

		Assert.That(answer.Value, Is.EqualTo("yes"));
	}
}
