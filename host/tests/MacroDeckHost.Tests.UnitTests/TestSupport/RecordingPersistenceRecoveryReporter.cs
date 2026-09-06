using MacroDeckHost.Application.Persistence;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingPersistenceRecoveryReporter : IPersistenceRecoveryReporter
{
	public List<PersistenceRecovery> Recoveries { get; } = [];

	public List<PersistenceLoss> Losses { get; } = [];

	public void ReportRecovered(PersistenceRecovery recovery) => Recoveries.Add(recovery);

	public void ReportUnrecoverable(PersistenceLoss loss) => Losses.Add(loss);
}
