using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Compatibility;
using Microsoft.Extensions.Logging;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Compatibility;

[TestFixture]
public class PluginCompatibilityServiceTests
{
	private const string PluginId = "com.example.plugin";

	private const string OtherPluginId = "com.example.other";

	private static readonly string[] _otherPluginOnly = [OtherPluginId];

	private RecordingLogger _logger = null!;
	private PluginCompatibilityService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_logger = new RecordingLogger();
		_service = new PluginCompatibilityService(_logger);
	}

	private static PluginCompatibilityEvaluation Evaluation(
		string pluginId = PluginId,
		string? sdkVersion = "1.0.0",
		int? protocolVersion = 1)
		=> new()
		{
			PluginId = pluginId,
			DisplayName = "Example",
			NegotiatedProtocolVersion = protocolVersion,
			Capabilities =
			[
				CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common")
			],
			Sdk = sdkVersion is null ? null : new PluginSdkUsage { SdkVersion = sdkVersion }
		};

	[Test]
	public void A_recorded_report_is_returned_by_Find_and_Snapshot()
	{
		var report = _service.Record(Evaluation());

		var found = _service.Find(PluginId);
		var snapshot = _service.Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.Not.Null);
			Assert.That(found!.PluginId, Is.EqualTo(PluginId));
			Assert.That(found.Report.State, Is.EqualTo(report.State));
			Assert.That(snapshot.Select(s => s.PluginId), Is.EquivalentTo(new[] { PluginId }));
			Assert.That(snapshot[0].Report.State, Is.EqualTo(report.State));
		});
	}

	[Test]
	public void An_unrecorded_plugin_is_not_found()
		=> Assert.That(_service.Find("com.example.never-seen"), Is.Null);

	[Test]
	public void Recording_again_replaces_the_previous_verdict()
	{
		// A plugin that reconnects after its capabilities started negotiating must not keep showing the
		// old, worse verdict.
		_service.Record(Evaluation());

		_service.Record(Evaluation() with { Capabilities = [] });

		Assert.Multiple(() =>
		{
			Assert.That(_service.Snapshot(), Has.Count.EqualTo(1));
			Assert.That(_service.Find(PluginId)!.Report.State,
				Is.Not.EqualTo(PluginCompatibilityStates.PartiallyIncompatible));
		});
	}

	[Test]
	public void Clear_forgets_the_plugin()
	{
		_service.Record(Evaluation());

		_service.Clear(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_service.Find(PluginId), Is.Null);
			Assert.That(_service.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public void Clear_leaves_other_plugins_alone()
	{
		_service.Record(Evaluation());
		_service.Record(Evaluation(pluginId: OtherPluginId));

		_service.Clear(PluginId);

		Assert.That(_service.Snapshot().Select(s => s.PluginId), Is.EquivalentTo(_otherPluginOnly));
	}


	[Test]
	public void The_findings_of_one_plugin_are_logged_once_however_often_it_reconnects()
	{
		_service.Record(Evaluation());
		var afterFirst = _logger.Entries.Count;

		_service.Record(Evaluation());
		_service.Record(Evaluation());

		Assert.Multiple(() =>
		{
			Assert.That(afterFirst, Is.GreaterThan(0), "the first evaluation must say something");
			Assert.That(_logger.Entries, Has.Count.EqualTo(afterFirst));
		});
	}

	[Test]
	public void A_plugin_reporting_a_different_sdk_version_is_logged_again()
	{
		_service.Record(Evaluation(sdkVersion: "1.0.0"));
		var afterFirst = _logger.Entries.Count;

		_service.Record(Evaluation(sdkVersion: "2.0.0"));

		Assert.That(_logger.Entries, Has.Count.GreaterThan(afterFirst));
	}

	[Test]
	public void Clearing_a_plugin_also_forgets_what_was_logged_for_it()
	{
		_service.Record(Evaluation());
		var afterFirst = _logger.Entries.Count;

		_service.Clear(PluginId);
		_service.Record(Evaluation());

		Assert.That(_logger.Entries, Has.Count.GreaterThan(afterFirst));
	}

	[Test]
	public void Two_plugins_with_the_same_findings_are_both_logged()
	{
		// The de-duplication is per plugin: one noisy plugin must not silence the report about another.
		_service.Record(Evaluation());
		var afterFirst = _logger.Entries.Count;

		_service.Record(Evaluation(pluginId: OtherPluginId));

		Assert.That(_logger.Entries, Has.Count.GreaterThan(afterFirst));
	}

	private sealed class RecordingLogger : ILogger<PluginCompatibilityService>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull
			=> null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter)
			=> Entries.Add((logLevel, formatter(state, exception)));
	}
}
