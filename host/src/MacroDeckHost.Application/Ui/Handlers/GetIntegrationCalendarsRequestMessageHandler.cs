using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationCalendarsRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationCalendarsRequest, GetIntegrationCalendarsResponse>
{
	private readonly IIntegrationRegistry _integrations;
	private readonly ICalendarEventCache _cache;

	public GetIntegrationCalendarsRequestMessageHandler(IIntegrationRegistry integrations, ICalendarEventCache cache)
	{
		_integrations = integrations;
		_cache = cache;
	}

	public ValueTask<GetIntegrationCalendarsResponse> Handle(
		GetIntegrationCalendarsRequest request,
		CancellationToken cancellationToken)
	{
		var integration = IntegrationCapabilityData.Find(_integrations, request.IntegrationId);
		if (integration is null)
		{
			return ValueTask.FromResult(new GetIntegrationCalendarsResponse());
		}

		var snapshot = _cache.Snapshot;
		var hasWindow = snapshot.WindowEnd > snapshot.WindowStart;

		return ValueTask.FromResult(new GetIntegrationCalendarsResponse
		{
			Found = true,
			Available = IntegrationCapabilityData.IsAvailable(_integrations, integration),
			WindowStart = hasWindow ? snapshot.WindowStart : null,
			WindowEnd = hasWindow ? snapshot.WindowEnd : null,
			Accounts =
			[
				.. snapshot.Accounts
					.Where(state => state.Account.IntegrationId == integration.Id)
					.Select(state => new IntegrationCalendarAccountDto
					{
						AccountId = state.Account.AccountId,
						DisplayName = state.Account.DisplayName,
						Status = state.Status,
						Calendars =
						[
							.. state.Calendars.Select(calendar => new IntegrationCalendarDto
							{
								Key = calendar.Key,
								Name = calendar.Name,
								Color = calendar.Color,
								IsPrimary = calendar.IsPrimary
							})
						]
					})
			],
			Events =
			[
				.. snapshot.Events
					.Where(summary => summary.IntegrationId == integration.Id)
					.Select(summary => new IntegrationCalendarEventDto
					{
						InstanceKey = summary.InstanceKey,
						AccountId = summary.AccountId,
						CalendarKey = summary.CalendarKey,
						Title = summary.Title,
						Start = summary.Start,
						End = summary.End,
						IsAllDay = summary.IsAllDay,
						Location = summary.Location,
						HasMeetingUrl = !string.IsNullOrEmpty(summary.MeetingUrl)
					})
			]
		});
	}
}
