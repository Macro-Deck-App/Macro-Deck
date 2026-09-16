namespace MacroDeckHost.Application.Plugins.Runtime;

public enum DotnetRollForward
{
	Minor,
	Major,
	LatestPatch,
	LatestMinor,
	LatestMajor,
	Disable
}

public sealed record DotnetFrameworkRequirement
{
	public const string NetCoreAppFramework = "Microsoft.NETCore.App";

	public required string Name { get; init; }

	public required Version Version { get; init; }

	public DotnetRollForward RollForward { get; init; } = DotnetRollForward.Minor;

	public static DotnetFrameworkRequirement ForNetCoreApp(string majorMinor) => new()
	{
		Name = NetCoreAppFramework,
		Version = System.Version.Parse(majorMinor)
	};

	public bool IsSatisfiedBy(IReadOnlyList<Version> installed)
	{
		var required = Normalize(Version);

		return installed.Select(Normalize).Any(candidate => RollForward switch
		{
			DotnetRollForward.Disable => candidate == required,
			DotnetRollForward.LatestPatch => candidate.Major == required.Major &&
				candidate.Minor == required.Minor &&
				candidate >= required,
			DotnetRollForward.Minor or DotnetRollForward.LatestMinor => candidate.Major == required.Major &&
				candidate >= required,
			_ => candidate >= required
		});
	}

	private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}

public sealed record DotnetMuxer
{
	public required string ExecutablePath { get; init; }

	// Null when the frameworks could not be determined; such a muxer is launched anyway.
	public IReadOnlyDictionary<string, IReadOnlyList<Version>>? InstalledFrameworks { get; init; }
}

public sealed record DotnetMuxerSelection
{
	public required DotnetMuxer Muxer { get; init; }

	public DotnetFrameworkRequirement? UnmetRequirement { get; init; }
}

public interface IDotnetMuxerLocator
{
	DotnetMuxerSelection? Locate(IReadOnlyList<DotnetFrameworkRequirement> requirements);

	void Invalidate();
}
