using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Calendar;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.MusicPlayer;
using MacroDeckHost.Integrations.System;
using MacroDeck.Sdk;

namespace MacroDeckHost.Infrastructure.Integrations;

internal static class IntegrationGatewayBinder
{
	public static void Bind(IIntegration integration,
		IAdbGateway adbGateway,
		ICompanionGateway companionGateway,
		IVariableBindingStore bindingStore,
		IVariableRefreshSignal refreshSignal,
		IKnownAudioDeviceStore? knownAudioDevices = null,
		IVariablePollingInvalidationSignal? pollingInvalidation = null,
		CalendarHostServices? calendarServices = null,
		IMusicPlayerAlbumColor? albumColor = null)
	{
		if (albumColor is not null && integration is IMusicPlayerAlbumColorConsumer albumColorConsumer)
		{
			albumColorConsumer.UseAlbumColor(albumColor);
		}

		if (calendarServices is not null && integration is ICalendarHostServicesConsumer calendarConsumer)
		{
			calendarConsumer.UseCalendarServices(calendarServices);
		}

		if (knownAudioDevices is not null && integration is IKnownAudioDeviceStoreConsumer audioDeviceConsumer)
		{
			audioDeviceConsumer.UseKnownAudioDeviceStore(knownAudioDevices);
		}

		if (pollingInvalidation is not null && integration is IVariablePollingInvalidationConsumer invalidationConsumer)
		{
			invalidationConsumer.UseVariablePollingInvalidation(pollingInvalidation);
		}

		if (integration is IAdbGatewayConsumer consumer)
		{
			consumer.UseGateway(adbGateway);
		}

		if (integration is ICompanionGatewayConsumer companionConsumer)
		{
			companionConsumer.UseGateway(companionGateway);
		}

		if (integration is IHomeAssistantBindingStoreConsumer bindingStoreConsumer)
		{
			bindingStoreConsumer.UseBindingStore(bindingStore);
		}

		if (integration is IVariableRefreshSignalConsumer refreshConsumer)
		{
			refreshConsumer.UseVariableRefreshSignal(refreshSignal);
		}
	}
}
