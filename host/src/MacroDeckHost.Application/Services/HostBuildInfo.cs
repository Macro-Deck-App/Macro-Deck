using System.Reflection;

namespace MacroDeckHost.Application.Services;

public static class HostBuildInfo
{
	public static string? Commit { get; } = ParseCommit(ResolveInformationalVersion());

	public static string? BuildTimestamp { get; } = ResolveMetadata("BuildTimestamp");

	public static string? BuildNumber { get; } = ResolveMetadata("BuildNumber");

	private const int ShortCommitLength = 7;

	public static string? ParseCommit(string? informationalVersion)
	{
		if (string.IsNullOrWhiteSpace(informationalVersion))
		{
			return null;
		}

		var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
		if (metadataIndex < 0 || metadataIndex == informationalVersion.Length - 1)
		{
			return null;
		}

		var metadata = informationalVersion[(metadataIndex + 1)..];

		var separatorIndex = metadata.IndexOf('.', StringComparison.Ordinal);
		if (separatorIndex >= 0)
		{
			metadata = metadata[..separatorIndex];
		}

		if (metadata.Length == 0)
		{
			return null;
		}

		return metadata.Length > ShortCommitLength && IsHex(metadata)
			? metadata[..ShortCommitLength]
			: metadata;
	}

	private static bool IsHex(string value)
	{
		foreach (var character in value)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	private static string? ResolveInformationalVersion()
	{
		return ResolveAssembly()
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;
	}

	private static string? ResolveMetadata(string key)
	{
		var value = ResolveAssembly()
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == key)?
			.Value;

		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	private static Assembly ResolveAssembly()
	{
		return Assembly.GetEntryAssembly() ?? typeof(HostBuildInfo).Assembly;
	}
}
