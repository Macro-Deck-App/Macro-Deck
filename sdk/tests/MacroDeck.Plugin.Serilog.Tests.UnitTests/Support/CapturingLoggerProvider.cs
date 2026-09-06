using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>Stands in for the console provider a plugin built from <c>WebApplication.CreateBuilder</c>
/// already has registered, so a test can assert what such a provider receives without reading the real
/// console.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
	private readonly List<string> _messages = [];
	private readonly Lock _gate = new();

	public IReadOnlyList<string> Messages
	{
		get
		{
			lock (_gate)
			{
				return [.. _messages];
			}
		}
	}

	public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

	public void Dispose()
	{
	}

	private void Add(string message)
	{
		lock (_gate)
		{
			_messages.Add(message);
		}
	}

	private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter) => provider.Add(formatter(state, exception));
	}
}
