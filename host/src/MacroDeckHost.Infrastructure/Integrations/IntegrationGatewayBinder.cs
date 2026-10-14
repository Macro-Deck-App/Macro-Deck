using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Infrastructure.SystemIntegrations.Calendar;
using MacroDeckHost.Infrastructure.SystemIntegrations.Email;
using MacroDeck.Sdk;

namespace MacroDeckHost.Infrastructure.Integrations;

internal static class IntegrationGatewayBinder
{
	public static void Bind(
		IIntegration integration,
		CalendarHostServices? calendarServices = null,
		EmailHostServices? emailServices = null)
	{
		if (calendarServices is not null && integration is ICalendarHostServicesConsumer calendarConsumer)
		{
			calendarConsumer.UseCalendarServices(calendarServices);
		}

		if (emailServices is not null && integration is IEmailHostServicesConsumer emailConsumer)
		{
			emailConsumer.UseEmailServices(emailServices);
		}
	}
}
