using System.Reflection;
using MacroDeck.Plugin.Packaging.Versioning;

namespace MacroDeckHost.Application.Services;

public static class HostVersion
{
	public const string FallbackVersion = "0.0.0-dev";

	public static string Current { get; } = ResolveCurrent();

	public static bool IsBeta { get; } = IsBetaVersion(Current);

	public static string Parse(string? informationalVersion)
	{
		if (string.IsNullOrWhiteSpace(informationalVersion))
		{
			return FallbackVersion;
		}

		var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
		return metadataIndex > 0
			? informationalVersion[..metadataIndex]
			: informationalVersion;
	}

	public static bool IsBetaVersion(string? version)
	{
		if (!SemanticVersion.TryParse(version, out var parsedVersion))
		{
			return false;
		}

		var identifiers = parsedVersion.PreReleaseIdentifiers;
		return identifiers.Count == 2 &&
			identifiers[0].Equals("beta", StringComparison.Ordinal) &&
			IsPositiveNumericIdentifier(identifiers[1]);
	}

	private static bool IsPositiveNumericIdentifier(string value)
	{
		return value.Length > 0 && value[0] != '0' && value.All(char.IsAsciiDigit);
	}

	private static string ResolveCurrent()
	{
		var assembly = Assembly.GetEntryAssembly() ?? typeof(HostVersion).Assembly;
		var informational = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;

		return Parse(informational);
	}
}
