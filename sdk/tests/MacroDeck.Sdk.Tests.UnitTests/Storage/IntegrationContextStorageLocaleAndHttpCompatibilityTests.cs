using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Http;
using MacroDeck.Sdk.Locale;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Storage;

[TestFixture]
public class IntegrationContextStorageLocaleAndHttpCompatibilityTests
{
	[Test]
	public void A_context_written_before_storage_existed_reads_every_key_as_missing_and_refuses_writes()
	{
		IIntegrationContext context = new ContextWrittenBeforeStorage();

		Assert.Multiple(async () =>
		{
			Assert.That((await context.Storage.ReadAsync("state.json")).Status,
				Is.EqualTo(IntegrationStorageReadStatus.Missing));
			Assert.ThrowsAsync<InvalidOperationException>(() => context.Storage.WriteAsync("state.json", "{}"));
			Assert.DoesNotThrowAsync(() => context.Storage.DeleteAsync("state.json"));
		});
	}

	[Test]
	public async Task A_context_written_before_the_locale_api_existed_answers_the_current_culture_with_a_24_hour_clock()
	{
		IIntegrationContext context = new ContextWrittenBeforeStorage();

		var format = await context.Locale.GetTimeOfDayFormatAsync();

		Assert.Multiple(() =>
		{
			Assert.That(format.Culture, Is.EqualTo(CultureInfo.CurrentCulture));
			Assert.That(format.HourCycle, Is.EqualTo(HourCycles.H23));
		});
	}

	[Test]
	public void A_context_written_before_http_existed_answers_the_sdk_default_user_agent()
	{
		IIntegrationContext context = new ContextWrittenBeforeStorage();

		Assert.That(context.Http.UserAgent,
			Is.EqualTo(DefaultHostHttp.Instance.UserAgent).And.StartsWith("MacroDeck/"));
	}

	[Test]
	public void A_config_flow_context_written_before_http_existed_answers_the_sdk_default_user_agent()
	{
		IConfigFlowContext context = new ConfigFlowContextWrittenBeforeHttp();

		Assert.That(context.Http.UserAgent, Is.EqualTo(DefaultHostHttp.Instance.UserAgent));
	}

	[TestCase("state.json", true)]
	[TestCase("a", true)]
	[TestCase("0-devices_v2.json", true)]
	[TestCase("", false)]
	[TestCase(".state", false)]
	[TestCase("-state", false)]
	[TestCase("State.json", false)]
	[TestCase("state json", false)]
	[TestCase("nested/state.json", false)]
	[TestCase("..", false)]
	[TestCase("state.json.tmp", false)]
	[TestCase("state.json.bak", false)]
	public void Keys_follow_the_documented_rules(string key, bool valid)
	{
		Assert.That(IntegrationStorageKeys.IsValid(key), Is.EqualTo(valid));
	}

	[Test]
	public void Keys_are_at_most_64_characters()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IntegrationStorageKeys.IsValid(new string('a', 64)), Is.True);
			Assert.That(IntegrationStorageKeys.IsValid(new string('a', 65)), Is.False);
		});
	}

	[Test]
	public void A_time_of_day_follows_the_hour_cycle_in_the_cultures_own_pattern()
	{
		var time = new TimeOnly(15, 5);

		Assert.Multiple(() =>
		{
			Assert.That(new TimeOfDayFormat(CultureInfo.GetCultureInfo("en-US"), HourCycles.H23).Format(time),
				Is.EqualTo("15:05"));
			Assert.That(new TimeOfDayFormat(CultureInfo.GetCultureInfo("en-US"), HourCycles.H12).Format(time),
				Does.Match(@"^3:05\sPM$"));
		});
	}

	[TestCase("en-US", "09:05")]
	[TestCase("ko-KR", "09:05")]
	[TestCase("hi-IN", "09:05")]
	[TestCase("zh-TW", "09:05")]
	[TestCase("de-DE", "09:05")]
	[TestCase("es-ES", "9:05")]
	[TestCase("cs-CZ", "9:05")]
	public void A_twenty_four_hour_clock_has_the_leading_zero_the_language_writes_one_with(
		string culture,
		string expected)
	{
		Assert.That(new TimeOfDayFormat(CultureInfo.GetCultureInfo(culture), HourCycles.H23).Format(new TimeOnly(9, 5)),
			Is.EqualTo(expected));
	}

	[TestCase("en-US")]
	[TestCase("ko-KR")]
	[TestCase("de-DE")]
	public void A_twelve_hour_clock_never_pads_the_hour(string culture)
	{
		Assert.That(new TimeOfDayFormat(CultureInfo.GetCultureInfo(culture), HourCycles.H12).Format(new TimeOnly(9, 5)),
			Does.Contain("9:05").And.Not.Contain("09:05"));
	}

	private sealed class ContextWrittenBeforeStorage : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class ConfigFlowContextWrittenBeforeHttp : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}
}
