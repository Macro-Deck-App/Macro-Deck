using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

internal sealed class TestBatteryIntegration : TestIntegration, IBatteryProvider
{
	public string ProviderName { get; init; } = string.Empty;

	public Func<IBatteryProviderContext, Task>? OnInitialize { get; init; }

	public IBatteryProviderContext? BatteryContext { get; private set; }

	public int ProviderInitializeCount { get; private set; }

	public int ProviderShutdownCount { get; private set; }

	public bool ProviderStoppedBeforeIntegration { get; private set; }

	public async Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default)
	{
		BatteryContext = context;
		ProviderInitializeCount++;
		if (OnInitialize is not null)
		{
			await OnInitialize(context);
		}
	}

	public Task ShutdownAsync(CancellationToken cancellationToken = default)
	{
		ProviderShutdownCount++;
		ProviderStoppedBeforeIntegration = ShutdownCount == 0;
		return Task.CompletedTask;
	}
}
