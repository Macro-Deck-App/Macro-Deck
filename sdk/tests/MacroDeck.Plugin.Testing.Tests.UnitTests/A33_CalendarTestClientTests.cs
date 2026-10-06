using System.Text.Json.Nodes;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Calendar;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A33_CalendarTestClientTests
{
	private static readonly DateTimeOffset _start = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(2));

	private static void Configure(PluginHostBuilder builder)
		=> builder.RegisterIntegration(_ => new CalendarIntegration());

	[Test]
	public async Task The_harness_and_the_wire_answer_every_calendar_read_alike()
	{
		await using var harness = PluginTestHarness.Create(Configure);

		await using var host = await MacroDeckTestHost.StartAsync();
		var hostBuilder = MacroDeckPlugin.CreatePlugin();
		Configure(hostBuilder);
		await using var plugin = await host.HostAsync(hostBuilder);
		var session = await host.WaitForSessionAsync();

		var events = new CalendarEventsArguments { AccountId = "alice", From = _start, To = _start.AddDays(1) };
		var details = new CalendarEventArguments { AccountId = "alice", CalendarId = "primary", EventId = "standup" };

		var harnessEvents = await harness.Calendar.GetEventsAsync(events);

		Assert.That(harnessEvents.DataAs<CalendarEventsResult>()!.Events.Single().Title, Is.EqualTo("Stand-up"));

		await AssertSameOutcome(harness.Calendar.DescribeAsync(), session.Calendar.DescribeAsync());
		await AssertSameOutcome(harness.Calendar.GetAccountsAsync(), session.Calendar.GetAccountsAsync());
		await AssertSameOutcome(harness.Calendar.GetCalendarsAsync(new CalendarAccountArguments { AccountId = "alice" }),
			session.Calendar.GetCalendarsAsync(new CalendarAccountArguments { AccountId = "alice" }));
		await AssertSameOutcome(Task.FromResult(harnessEvents), session.Calendar.GetEventsAsync(events));
		await AssertSameOutcome(harness.Calendar.GetEventAsync(details), session.Calendar.GetEventAsync(details));
	}

	private static async Task AssertSameOutcome(
		Task<CapabilityInvocationOutcome> viaHarness,
		Task<CapabilityInvocationOutcome> viaHost)
	{
		var harnessOutcome = await viaHarness;
		var hostOutcome = await viaHost;

		Assert.Multiple(() =>
		{
			Assert.That(harnessOutcome.Succeeded, Is.True, harnessOutcome.Error?.Message);
			Assert.That(hostOutcome.Succeeded, Is.True, hostOutcome.Error?.Message);
			Assert.That(JsonNode.DeepEquals(JsonNode.Parse(harnessOutcome.Data!.Value.GetRawText()),
					JsonNode.Parse(hostOutcome.Data!.Value.GetRawText())),
				Is.True);
		});
	}

	private sealed class CalendarIntegration : IPluginIntegration, ICalendarProvider
	{
		private static readonly CalendarEvent _standup = new()
		{
			Id = "standup",
			CalendarId = "primary",
			Title = "Stand-up",
			Start = _start,
			End = _start.AddMinutes(15),
			Description = "Daily",
			Participants = [new CalendarParticipant { Name = "Bob", Response = CalendarResponseStatus.Accepted }]
		};

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IReadOnlyList<CalendarAccount> GetAccounts()
			=> [new CalendarAccount { Id = "alice", DisplayName = "alice@example.com" }];

		public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(
			string accountId,
			CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<CalendarInfo>>([new CalendarInfo { Id = "primary", Name = "Alice" }]);

		public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
			string accountId,
			CalendarEventQuery query,
			CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<CalendarEvent>>([_standup]);

		public Task<CalendarEvent?> GetEventAsync(
			string accountId,
			string calendarId,
			string eventId,
			CancellationToken cancellationToken)
			=> Task.FromResult<CalendarEvent?>(eventId == _standup.Id ? _standup : null);
	}
}
