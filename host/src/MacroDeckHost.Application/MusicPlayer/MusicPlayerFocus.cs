namespace MacroDeckHost.Application.MusicPlayer;

public static class MusicPlayerFocus
{
	public static string? Resolve(IReadOnlyList<MusicPlayerInstanceDescriptor> instances, string? activeInstanceId)
	{
		if (activeInstanceId is not null &&
			instances.Any(instance => string.Equals(instance.InstanceId, activeInstanceId, StringComparison.Ordinal)))
		{
			return activeInstanceId;
		}

		return instances.Count > 0 ? instances[0].InstanceId : null;
	}
}
