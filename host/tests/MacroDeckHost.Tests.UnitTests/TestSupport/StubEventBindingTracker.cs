using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class StubEventBindingTracker : IEventBindingTracker
{
	public IReadOnlyList<EventBindingDto> BindingsFor(string providerId)
	{
		return [];
	}

	public IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged)
	{
		return new Nothing();
	}

	public Task FlushAsync()
	{
		return Task.CompletedTask;
	}

	private sealed class Nothing : IDisposable
	{
		public void Dispose()
		{
		}
	}
}
