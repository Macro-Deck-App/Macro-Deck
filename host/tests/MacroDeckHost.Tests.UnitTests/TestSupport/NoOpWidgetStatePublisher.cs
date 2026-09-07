using MacroDeckHost.Application.Rendering;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

public sealed class NoOpWidgetStatePublisher : IWidgetStatePublisher
{
	public Task PublishIfChanged(Guid widgetId,
		WidgetStateReconciliation result,
		CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
