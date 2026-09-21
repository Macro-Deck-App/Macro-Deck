using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.BackgroundServices;
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
		var service = new VariableInitializeBackgroundService(lifetime,
			new VariableRegistry(),
			new FailingUserVariableStore(),
			readiness,
			Log.Logger);

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
