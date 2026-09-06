using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Compatibility;
using Microsoft.Extensions.Logging;

namespace MacroDeckHost.Application.Plugins.Compatibility;

public sealed record PluginCompatibilitySnapshot
{
	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public required PluginCompatibilityReport Report { get; init; }
}

public interface IPluginCompatibilityService
{
	PluginCompatibilityReport Record(PluginCompatibilityEvaluation evaluation);

	IReadOnlyList<PluginCompatibilitySnapshot> Snapshot();

	PluginCompatibilitySnapshot? Find(string pluginId);

	void Clear(string pluginId);
}

public class PluginCompatibilityService : IPluginCompatibilityService
{
	private readonly ILogger<PluginCompatibilityService> _logger;

	private readonly ConcurrentDictionary<string, PluginCompatibilitySnapshot> _byPluginId =
		new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, byte> _logged = new(StringComparer.Ordinal);

	public PluginCompatibilityService(ILogger<PluginCompatibilityService> logger)
	{
		_logger = logger;
	}

	public PluginCompatibilityReport Record(PluginCompatibilityEvaluation evaluation)
	{
		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		_byPluginId[evaluation.PluginId] = new PluginCompatibilitySnapshot
		{
			PluginId = evaluation.PluginId,
			DisplayName = evaluation.DisplayName,
			Report = report
		};

		LogOnce(evaluation, report);

		return report;
	}

	public IReadOnlyList<PluginCompatibilitySnapshot> Snapshot() => [.. _byPluginId.Values];

	public PluginCompatibilitySnapshot? Find(string pluginId)
		=> _byPluginId.TryGetValue(pluginId, out var snapshot) ? snapshot : null;

	public void Clear(string pluginId)
	{
		_byPluginId.TryRemove(pluginId, out _);

		foreach (var key in _logged.Keys.Where(key
			=> key.StartsWith(pluginId + "|", StringComparison.Ordinal)))
		{
			_logged.TryRemove(key, out _);
		}
	}

	private void LogOnce(PluginCompatibilityEvaluation evaluation, PluginCompatibilityReport report)
	{
		if (report.State == PluginCompatibilityStates.Compatible)
		{
			return;
		}

		var version = report.SdkVersion ?? "unknown";

		if (_logged.TryAdd($"{evaluation.PluginId}|{version}|state|{report.State}", 0))
		{
			PluginCompatibilityLog.CompatibilityState(_logger,
				evaluation.PluginId,
				report.State,
				report.Findings.Count,
				report.UsageSource);
		}

		foreach (var finding in report.Findings)
		{
			if (_logged.TryAdd($"{evaluation.PluginId}|{version}|{finding.DiagnosticId}|{finding.Subject}", 0))
			{
				PluginCompatibilityLog.Finding(_logger,
					evaluation.PluginId,
					finding.DiagnosticId,
					finding.Subject,
					finding.Guidance);
			}
		}
	}
}
