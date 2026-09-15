using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.System;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationGatewayBinderAudioTests
{
	[Test]
	public void Bind_gives_the_known_audio_device_store_and_the_polling_invalidation_to_a_consumer()
	{
		var store = new EmptyKnownAudioDeviceStore();
		var invalidation = new VariablePollingInvalidationSignal();
		var integration = new AudioConsumerIntegration();

		IntegrationGatewayBinder.Bind(integration,
			null!,
			null!,
			new EmptyBindingStore(),
			new VariableRefreshSignal(),
			store,
			invalidation);

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedStore, Is.SameAs(store));
			Assert.That(integration.ReceivedInvalidation, Is.SameAs(invalidation));
		});
	}

	private sealed class AudioConsumerIntegration
		: IIntegration, IKnownAudioDeviceStoreConsumer, IVariablePollingInvalidationConsumer
	{
		public string Id => "test.audio-consumer";
		public LocalizedText Name => "Audio Consumer";
		public string Version => "1.0.0";
		public bool IsInitialized => false;
		public IReadOnlyList<IActionDefinition> Actions => [];
		public IKnownAudioDeviceStore? ReceivedStore { get; private set; }
		public IVariablePollingInvalidationSignal? ReceivedInvalidation { get; private set; }

		public void UseKnownAudioDeviceStore(IKnownAudioDeviceStore store) => ReceivedStore = store;

		public void UseVariablePollingInvalidation(IVariablePollingInvalidationSignal signal)
			=> ReceivedInvalidation = signal;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class EmptyKnownAudioDeviceStore : IKnownAudioDeviceStore
	{
		public bool TryLoad(out IReadOnlyList<KnownAudioDevice> devices)
		{
			devices = [];
			return true;
		}

		public bool Save(IEnumerable<KnownAudioDevice> devices) => true;
	}

	private sealed class EmptyBindingStore : IVariableBindingStore
	{
		public IReadOnlyList<VariableBinding> Load() => [];

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = [];
			return true;
		}

		public bool Save(IEnumerable<VariableBinding> bindings) => true;
	}
}
