using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.Twitch;
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
		ITwitchChatSink? twitchChatSink = null,
		ITwitchStatsSink? twitchStatsSink = null)
	{
		if (twitchStatsSink is not null && integration is ITwitchStatsSinkConsumer statsConsumer)
		{
			statsConsumer.UseTwitchStatsSink(twitchStatsSink);
		}

		if (twitchChatSink is not null && integration is ITwitchChatSinkConsumer chatConsumer)
		{
			chatConsumer.UseTwitchChatSink(twitchChatSink);
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
