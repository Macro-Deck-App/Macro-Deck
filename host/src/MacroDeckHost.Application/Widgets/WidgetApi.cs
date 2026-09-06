using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Widgets;

public sealed class WidgetApi : IWidgetApi
{
	private readonly IServiceScopeFactory _scopeFactory;

	public WidgetApi(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public IReadOnlyList<WidgetTargetInfo> GetWidgets()
	{
		using var scope = _scopeFactory.CreateScope();
		return scope.ServiceProvider.GetRequiredService<IWidgetAppearanceService>().GetWidgets();
	}

	public bool Exists(string widgetId)
	{
		using var scope = _scopeFactory.CreateScope();
		return scope.ServiceProvider.GetRequiredService<IWidgetAppearanceService>().Exists(widgetId);
	}

	public async Task<bool> ApplyAsync(
		WidgetAppearanceRequest request,
		CancellationToken cancellationToken = default)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<IWidgetAppearanceService>()
			.ApplyAsync(request, cancellationToken);
	}

	public async Task<WidgetStateWriteResult> SetStateAsync(
		string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(widgetId, out var id))
		{
			return WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<IActionButtonStateService>()
			.SetAsync(id, stateId, cancellationToken);
	}

	public async Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
		CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(widgetId, out var id))
		{
			return WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<IActionButtonStateService>()
			.AdvanceAsync(id, cancellationToken);
	}
}
