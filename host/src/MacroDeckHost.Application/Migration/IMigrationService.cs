using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Migration;

public sealed record MigrationSourceDescriptor(string Id, string Name, string? DefaultPath);

public sealed record MigrationOutcome(MigrationPlan Plan, IReadOnlyList<Guid> CreatedProfileIds);

public interface IMigrationService
{
	IReadOnlyList<MigrationSourceDescriptor> GetSources();

	Task<Result<MigrationPlan, MigrationError>> Preview(MigrationRequest request,
		CancellationToken cancellationToken);

	Task<Result<MigrationOutcome, MigrationError>> Apply(MigrationRequest request,
		CancellationToken cancellationToken);
}
