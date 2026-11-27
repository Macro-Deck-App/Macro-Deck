using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

public sealed class KeyRingLateKeystoreAnswerServiceTests
{
	private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

	private RecordingRestartService _restart = null!;
	private CapturingSink _sink = null!;
	private Logger _logger = null!;
	private string _marker = null!;

	[SetUp]
	public void SetUp()
	{
		_restart = new RecordingRestartService();
		_sink = new CapturingSink();
		_logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
		_marker = KeyRingLateKeystoreAnswerService.MarkerPath(Guid.NewGuid());
	}

	[TearDown]
	public void TearDown()
	{
		_logger.Dispose();
		File.Delete(_marker);
	}

	[Test]
	public async Task The_ring_key_arriving_late_restarts_the_host_once()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var late = new TaskCompletionSource<KekStoreReadResult>();
		using var service = Service(late.Task, KeyRingKeyDerivation.DeriveKekId(kek));
		await service.StartAsync(CancellationToken.None);

		late.SetResult(KekStoreReadResult.Found(kek.ToArray()));
		await service.ExecuteTask!.WaitAsync(_wait);

		Assert.That(_restart.Reasons, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task The_start_after_a_late_answer_restart_is_told_to_wait_longer_exactly_once()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		using var service = Service(Task.FromResult(KekStoreReadResult.Found(kek.ToArray())),
			KeyRingKeyDerivation.DeriveKekId(kek));
		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!.WaitAsync(_wait);

		var first = KeyRingLateKeystoreAnswerService.ConsumeMarker(_marker);
		var second = KeyRingLateKeystoreAnswerService.ConsumeMarker(_marker);

		Assert.That((first, second), Is.EqualTo((true, false)));
	}

	[Test]
	public async Task A_late_answer_that_cannot_unlock_the_ring_does_not_restart()
	{
		var other = KekStoreReadResult.Found(KeyRingKeyDerivation.CreateKek());
		var ringKekId = KeyRingKeyDerivation.DeriveKekId(KeyRingKeyDerivation.CreateKek());

		foreach (var answer in new[] { other, KekStoreReadResult.NotFound(), KekStoreReadResult.Unavailable("x") })
		{
			using var service = Service(Task.FromResult(answer), ringKekId);
			await service.StartAsync(CancellationToken.None);
			await service.ExecuteTask!.WaitAsync(_wait);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_restart.Reasons, Is.Empty);
			Assert.That(File.Exists(_marker), Is.False);
		});
	}

	[Test]
	public async Task Without_a_restart_the_log_says_a_manual_restart_is_needed()
	{
		_restart.Supported = false;
		var kek = KeyRingKeyDerivation.CreateKek();
		using var service = Service(Task.FromResult(KekStoreReadResult.Found(kek.ToArray())),
			KeyRingKeyDerivation.DeriveKekId(kek));

		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!.WaitAsync(_wait);

		Assert.That(_sink.Events.Count(e => e.Level == LogEventLevel.Warning), Is.EqualTo(1));
	}

	[Test]
	public async Task A_start_that_already_had_the_longer_wait_does_not_restart_again()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		using var service = Service(Task.FromResult(KekStoreReadResult.Found(kek.ToArray())),
			KeyRingKeyDerivation.DeriveKekId(kek),
			true);

		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!.WaitAsync(_wait);

		Assert.Multiple(() =>
		{
			Assert.That(_restart.Reasons, Is.Empty);
			Assert.That(_sink.Events.Count(e => e.Level == LogEventLevel.Warning), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Stopping_while_the_prompt_is_still_open_does_not_wait_for_it()
	{
		var neverAnswered = new TaskCompletionSource<KekStoreReadResult>();
		using var service = Service(neverAnswered.Task, "ring");
		await service.StartAsync(CancellationToken.None);

		await service.StopAsync(CancellationToken.None).WaitAsync(_wait);

		Assert.That(_restart.Reasons, Is.Empty);
	}

	private KeyRingLateKeystoreAnswerService Service(
		Task<KekStoreReadResult>? lateRead,
		string? ringKekId,
		bool alreadyRestarted = false)
	{
		return new KeyRingLateKeystoreAnswerService(
			lateRead is null ? null : KeyRingKeystoreRead.Evaluate(lateRead, ringKekId),
			_marker,
			alreadyRestarted,
			_restart,
			_logger);
	}

	private sealed class RecordingRestartService : IApplicationRestartService
	{
		public List<string> Reasons { get; } = [];

		public bool Supported { get; set; } = true;

		public RestartAvailability Availability => new(Supported, Supported ? null : "no shell");

		public bool RestartRequested => Reasons.Count > 0;

		public Result<RestartError> Request(string reason)
		{
			if (!Supported)
			{
				return Result.Fail(RestartError.NotSupported);
			}

			Reasons.Add(reason);

			return Result.Ok<RestartError>();
		}
	}

	private sealed class CapturingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent)
		{
			Events.Add(logEvent);
		}
	}
}
