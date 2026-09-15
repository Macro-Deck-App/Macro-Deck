using MacroDeckHost.Application.Persistence;

namespace MacroDeckHost.Integrations.System;

public interface IKnownAudioDeviceStoreConsumer
{
	void UseKnownAudioDeviceStore(IKnownAudioDeviceStore store);
}
