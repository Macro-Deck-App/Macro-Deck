namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreDependenciesBody
{
	public string? Ecosystem { get; set; }

	public string? SdkVersion { get; set; }

	public List<StoreDependencyFrameworkBody> Frameworks { get; set; } = [];
}

public class StoreDependencyFrameworkBody
{
	public string Framework { get; set; } = string.Empty;

	public List<StoreDependencyPackageBody> Packages { get; set; } = [];
}

public class StoreDependencyPackageBody
{
	public string Id { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;

	public string? RequestedVersion { get; set; }

	public bool Direct { get; set; }
}
