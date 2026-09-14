using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class StubEventBindingTracker : IEventBindingTracker
{
	public IReadOnlyList<EventBindingDto> BindingsFor(string providerId) => [];

	public IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged) => new Nothing();

	public Task FlushAsync() => Task.CompletedTask;

	private sealed class Nothing : IDisposable
	{
		public void Dispose()
		{
		}
	}
}
