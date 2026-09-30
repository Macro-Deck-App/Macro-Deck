using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>Stands in for the console provider a plugin built from <c>WebApplication.CreateBuilder</c>
/// already has registered, so a test can assert what such a provider receives without reading the real
/// console.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
	private readonly List<CapturedLogEntry> _entries = [];
	private readonly Lock _gate = new();

	public IReadOnlyList<CapturedLogEntry> Entries
	{
		get
		{
			lock (_gate)
			{
				return [.. _entries];
			}
		}
	}

	public IReadOnlyList<string> Messages => [.. Entries.Select(entry => entry.Message)];

	public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

	public void Dispose()
	{
	}

	private void Add(CapturedLogEntry entry)
	{
		lock (_gate)
		{
			_entries.Add(entry);
		}
	}

	private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter) =>
			provider.Add(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
	}
}

internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);
