using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;

namespace MacroDeckHost.Application.Store.Testing;

public interface IStoreTestService
{
	Task<StoreTestListResult> GetTests(CancellationToken cancellationToken = default);

	Task<StoreTestInstallResult> Install(string packageId,
		Guid buildId,
		bool consent,
		CancellationToken cancellationToken = default);
}

public sealed record StoreTestListResult(StorePlatformFailure Failure, IReadOnlyList<StoreTest> Tests)
{
	public bool Success => Failure == StorePlatformFailure.None;
}

public sealed record StoreTest
{
	public required StorePlatformTest Test { get; init; }

	public string? InstalledVersion { get; init; }

	public Guid? InstalledTestBuildId { get; init; }

	public string? StoreVersion { get; init; }

	public Guid? ActiveOperationId { get; init; }

	public bool HasIcon { get; init; }

	public string? IconSha256 { get; init; }
}

public sealed record StoreTestInstallResult(StorePlatformFailure Failure, StoreOperation? Operation = null)
{
	public bool Success => Failure == StorePlatformFailure.None;
}
