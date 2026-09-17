namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreTestsResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public List<StoreTestBody> Tests { get; set; } = [];
}

public class StoreTestBody
{
	public string PackageId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public DateTimeOffset JoinedAt { get; set; }

	public bool HasIcon { get; set; }

	public string? IconSha256 { get; set; }

	public string? InstalledVersion { get; set; }

	public Guid? InstalledTestBuildId { get; set; }

	public Guid? ActiveOperationId { get; set; }

	public List<StoreTestBuildBody> Builds { get; set; } = [];
}

public class StoreTestBuildBody
{
	public Guid Id { get; set; }

	public string Version { get; set; } = string.Empty;

	public string Build { get; set; } = string.Empty;

	public string? Changelog { get; set; }

	public long SizeInBytes { get; set; }

	public DateTimeOffset UploadedAt { get; set; }

	public DateTimeOffset AvailableAt { get; set; }
}

public class InstallStoreTestBuildRequest
{
	public string PackageId { get; set; } = string.Empty;

	public Guid BuildId { get; set; }

	// Set only after the user confirmed installing a build that was never reviewed; without it the host refuses.
	public bool Consent { get; set; }
}
