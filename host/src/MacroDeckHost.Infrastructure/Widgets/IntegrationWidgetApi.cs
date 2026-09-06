using MacroDeckHost.Application.Rendering;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Infrastructure.Widgets;

/// <summary>
/// Binds an in-process integration's identity to the single shared <see cref="IWidgetApi" /> instance,
/// exactly as <c>IntegrationVariableApi</c> binds <c>IIntegrationRegistry</c>'s per-integration identity to
/// the variable service: <c>IntegrationInitializer</c>'s shared <c>_widgetApi</c> field has no idea who is
/// calling, so <see cref="InvalidateIconAsync" /> - which must trust only the calling integration - needs
/// the integration id bound at construction rather than threaded through every call.
/// </summary>
public sealed class IntegrationWidgetApi : IWidgetApi
{
	private readonly string _integrationId;
	private readonly IWidgetApi _inner;
	private readonly IWidgetIconInvalidator _iconInvalidator;

	public IntegrationWidgetApi(string integrationId, IWidgetApi inner, IWidgetIconInvalidator iconInvalidator)
	{
		_integrationId = integrationId;
		_inner = inner;
		_iconInvalidator = iconInvalidator;
	}

	public IReadOnlyList<WidgetTargetInfo> GetWidgets() => _inner.GetWidgets();

	public bool Exists(string widgetId) => _inner.Exists(widgetId);

	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		=> _inner.ApplyAsync(request, cancellationToken);

	public Task<WidgetStateWriteResult> SetStateAsync(
		string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
		=> _inner.SetStateAsync(widgetId, stateId, cancellationToken);

	public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
		CancellationToken cancellationToken = default)
		=> _inner.AdvanceStateAsync(widgetId, cancellationToken);

	public Task InvalidateIconAsync(string actionId, CancellationToken cancellationToken = default)
	{
		_iconInvalidator.Invalidate(_integrationId, actionId);
		return Task.CompletedTask;
	}
}
