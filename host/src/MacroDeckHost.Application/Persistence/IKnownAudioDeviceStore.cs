namespace MacroDeckHost.Application.Persistence;

public sealed record KnownAudioDevice(string Flow, string DeviceId, string Key, string Slug, string Name);

public interface IKnownAudioDeviceStore
{
	bool TryLoad(out IReadOnlyList<KnownAudioDevice> devices);

	bool Save(IEnumerable<KnownAudioDevice> devices);
}
