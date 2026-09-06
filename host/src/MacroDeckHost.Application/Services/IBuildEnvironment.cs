using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Services;

public interface IBuildEnvironment
{
	string Version { get; }

	bool IsBeta { get; }

	BuildChannel Channel { get; }
}

public sealed class BuildEnvironment : IBuildEnvironment
{
	public string Version => HostVersion.Current;

	public bool IsBeta => HostVersion.IsBeta;

	public BuildChannel Channel => BuildConfig.Channel;
}
