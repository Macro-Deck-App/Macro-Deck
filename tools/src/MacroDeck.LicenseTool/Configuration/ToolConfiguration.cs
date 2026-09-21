using YamlDotNet.Serialization;

namespace MacroDeck.LicenseTool.Configuration;

internal sealed class ToolConfiguration
{
	public string NoticeHeader { get; set; } = "";

	public PolicyConfiguration Policy { get; set; } = new();

	[YamlMember(Alias = "nuget")]
	public List<NuGetSourceConfiguration> NuGet { get; set; } = [];

	public NpmSourceConfiguration? Npm { get; set; }

	public List<CargoSourceConfiguration> Cargo { get; set; } = [];

	public AssetsConfiguration? Assets { get; set; }
}

internal sealed class AssetsConfiguration
{
	public List<string> Roots { get; set; } = [];

	public List<string> Extensions { get; set; } = [];

	public List<string> FirstParty { get; set; } = [];
}

internal sealed class PolicyConfiguration
{
	public List<string> AllowedLicenses { get; set; } = [];

	public List<string> AllowedExceptions { get; set; } = [];
}

internal sealed class NuGetSourceConfiguration
{
	public string Project { get; set; } = "";

	public string TargetFramework { get; set; } = "";

	public List<PlatformTargetConfiguration> RuntimeIdentifiers { get; set; } = [];
}

internal sealed class PlatformTargetConfiguration
{
	public string Id { get; set; } = "";

	public string Platform { get; set; } = "";
}

internal sealed class NpmSourceConfiguration
{
	public string Root { get; set; } = "";

	public List<NpmWorkspaceConfiguration> Workspaces { get; set; } = [];
}

internal sealed class NpmWorkspaceConfiguration
{
	public string Path { get; set; } = "";

	public string? Tsconfig { get; set; }

	public List<string> Entries { get; set; } = [];

	public List<string> StyleRoots { get; set; } = [];
}

internal sealed class CargoSourceConfiguration
{
	public string ManifestDirectory { get; set; } = "";

	public List<PlatformTargetConfiguration> Targets { get; set; } = [];
}

internal sealed class OverridesFile
{
	public List<PackageOverride> Packages { get; set; } = [];
}

internal sealed class PackageOverride
{
	public string Ecosystem { get; set; } = "";

	public string Name { get; set; } = "";

	public string? License { get; set; }

	public string? LicenseFile { get; set; }

	public string? Reviewed { get; set; }

	public string? Notice { get; set; }

	public string? Exclude { get; set; }

	public string? Url { get; set; }

	public string? Copyright { get; set; }

	public List<AdditionalLicense> AdditionalLicenses { get; set; } = [];
}

internal sealed class AdditionalLicense
{
	public string Title { get; set; } = "";

	public string License { get; set; } = "";

	public string? LicenseFile { get; set; }

	public string? Copyright { get; set; }
}

internal sealed class AttributionsFile
{
	public List<AssetAttribution> Assets { get; set; } = [];
}

internal sealed class AssetAttribution
{
	public string Name { get; set; } = "";

	public string? Url { get; set; }

	public string License { get; set; } = "";

	public string? Copyright { get; set; }

	public string? LicenseFile { get; set; }

	public string? Reviewed { get; set; }

	public string? Notice { get; set; }

	public string? Note { get; set; }

	public List<string> Paths { get; set; } = [];
}
