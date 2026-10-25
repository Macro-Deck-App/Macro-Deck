using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.VideoStreams;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupProgressReporterTests
{
	private ManualTimeProvider _time = null!;
	private RecordingPublisher _publisher = null!;
	private ServiceProvider _services = null!;
	private BackupProgressReporter _reporter = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_publisher = new RecordingPublisher();
		_services = new ServiceCollection().AddSingleton<IPublisher>(_publisher).BuildServiceProvider();
		_reporter = new BackupProgressReporter(_services.GetRequiredService<IServiceScopeFactory>(), _time);
	}

	[TearDown]
	public void TearDown()
	{
		_services.Dispose();
	}

	[Test]
	public void A_new_operation_replaces_a_finished_one()
	{
		_reporter.Report(Status(Guid.NewGuid(), BackupOperationStage.Completed, 1));
		var next = Guid.NewGuid();

		_reporter.Report(Status(next, BackupOperationStage.Preparing, null));

		Assert.That(_reporter.Current.OperationId, Is.EqualTo(next));
	}

	[Test]
	public void Stage_changes_are_published_in_the_order_they_were_reported()
	{
		var operationId = Guid.NewGuid();
		BackupOperationStage[] stages =
		[
			BackupOperationStage.Preparing,
			BackupOperationStage.CreatingSnapshot,
			BackupOperationStage.Saving,
			BackupOperationStage.Validating,
			BackupOperationStage.Completed
		];

		foreach (var stage in stages)
		{
			_reporter.Report(Status(operationId, stage, null));
		}

		Assert.That(() => PublishedStages(), Is.EqualTo(stages).After(2000, 10));
	}

	[Test]
	public void The_eta_is_unknown_until_a_rate_could_be_measured()
	{
		var operationId = Guid.NewGuid();
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0));
		_time.Advance(TimeSpan.FromSeconds(2));
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0.05));

		Assert.That(_reporter.Current.EtaSeconds, Is.Null);
	}

	[Test]
	public void The_eta_follows_the_measured_rate()
	{
		var operationId = Guid.NewGuid();
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0));
		_time.Advance(TimeSpan.FromSeconds(10));

		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0.25));

		Assert.That(_reporter.Current.EtaSeconds, Is.EqualTo(30));
	}

	[Test]
	public void The_eta_is_measured_again_after_a_stage_change()
	{
		var operationId = Guid.NewGuid();
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0));
		_time.Advance(TimeSpan.FromSeconds(10));
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0.5));

		_reporter.Report(Status(operationId, BackupOperationStage.Validating, 0.8));

		Assert.That(_reporter.Current.EtaSeconds, Is.Null);
	}

	[Test]
	public void A_finished_operation_has_no_eta()
	{
		var operationId = Guid.NewGuid();
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0));
		_time.Advance(TimeSpan.FromSeconds(10));
		_reporter.Report(Status(operationId, BackupOperationStage.Saving, 0.5));

		_reporter.Report(Status(operationId, BackupOperationStage.Completed, 1));

		Assert.That(_reporter.Current.EtaSeconds, Is.Null);
	}

	private BackupOperationStatus Status(Guid operationId, BackupOperationStage stage, double? fraction)
	{
		return new BackupOperationStatus(operationId,
			BackupOperationKind.Create,
			stage,
			BackupTrigger.Manual,
			fraction is null ? null : (int)(fraction * 100),
			null,
			null,
			null,
			null,
			null,
			_time.GetUtcNow()) { ProgressFraction = fraction };
	}

	private BackupOperationStage[] PublishedStages()
	{
		return [.. _publisher.Of<BackupOperationProgressNotification>().Select(n => n.Status.Stage)];
	}
}
