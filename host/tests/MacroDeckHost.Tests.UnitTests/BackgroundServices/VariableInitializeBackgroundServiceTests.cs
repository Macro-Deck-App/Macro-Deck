using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class VariableInitializeBackgroundServiceTests
{
	[Test]
	public async Task A_failed_variable_load_stops_the_host_instead_of_leaving_it_never_ready()
	{
		var readiness = new StartupReadiness();
		var lifetime = new RecordingHostLifetime();
		var registry = new VariableRegistry();
		var service = new VariableInitializeBackgroundService(lifetime,
			registry,
			new FailingUserVariableStore(),
			readiness,
			Log.Logger,
			new FileVariableSynchronizer(registry, new RecordingMediator(), new FakeVariableFileSystem(), Log.Logger));

		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!;
		await service.StopAsync(CancellationToken.None);
		readiness.MarkCachesReady();

		Assert.Multiple(() =>
		{
			Assert.That(lifetime.StopRequested, Is.True);
			Assert.That(readiness.IsReady, Is.False);
		});
	}

	[Test]
	public async Task A_variable_read_from_a_file_starts_with_the_files_content_and_announces_no_change()
	{
		var path = Path.Combine(Path.GetTempPath(), "md-load", "counter.txt");
		var files = new FakeVariableFileSystem();
		files.SetFile(path, "42\n", notify: false);
		var missingPath = Path.Combine(Path.GetTempPath(), "md-load", "missing.txt");
		var stored = new[]
		{
			FileVariable("counter", VariableType.Numeric, path),
			FileVariable("status", VariableType.Text, missingPath)
		};
		var registry = new VariableRegistry();
		var mediator = new RecordingMediator();
		var readiness = new StartupReadiness();
		var synchronizer = new FileVariableSynchronizer(registry, mediator, files, Log.Logger);
		var service = new VariableInitializeBackgroundService(new RecordingHostLifetime(),
			registry,
			new StoredUserVariableStore(stored),
			readiness,
			Log.Logger,
			synchronizer);

		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!;
		await synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetById(stored[0].Id)!.Value, Is.EqualTo("42"));
			Assert.That(registry.IsAvailable(stored[0].Id), Is.True);
			Assert.That(registry.IsAvailable(stored[1].Id), Is.False);
			Assert.That(mediator.Published, Is.Empty);
			Assert.That(files.WatcherCount(path), Is.EqualTo(1));
		});

		files.SetFile(path, "43");
		await synchronizer.WhenIdleAsync();

		Assert.That(registry.GetById(stored[0].Id)!.Value, Is.EqualTo("43"));
	}

	private static VariableEntity FileVariable(string name, VariableType type, string path) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		FileSource = new VariableFileSource(path, false)
	};

	private sealed class StoredUserVariableStore(IReadOnlyList<VariableEntity> stored) : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => stored;

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	private sealed class FailingUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => throw new IOException("variables file unreadable");

		public void Save(IEnumerable<VariableEntity> userVariables) => throw new NotSupportedException();
	}

	private sealed class RecordingHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public bool StopRequested { get; private set; }

		public void StopApplication() => StopRequested = true;
	}
}
