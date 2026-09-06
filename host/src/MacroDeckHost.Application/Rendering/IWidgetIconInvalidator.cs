using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// Fans an action's "my icon changed" push out to exactly the widgets currently following that action,
/// re-queuing them for an immediate re-read instead of waiting for the next poll tick - the same pattern
/// <c>IntegrationStateChangedNotificationHandler</c> uses for a state provider's availability flip. Shared
/// by <c>IntegrationWidgetApi</c> (in-process, integration id bound at construction) and
/// <c>PluginCallbackRouter</c> (out-of-process, integration id taken from the connection), so both push
/// paths can never disagree about which widgets an invalidation reaches.
/// </summary>
public interface IWidgetIconInvalidator
{
	/// <param name="integrationId">The calling integration - the trust boundary: an integration can only
	/// ever invalidate its own actions.</param>
	/// <param name="actionId">The action's declared local id, not a configured instance - a configured
	/// instance has no wire identity, so every widget following any instance of this action is re-read.</param>
	void Invalidate(string integrationId, string actionId);
}

public sealed class WidgetIconInvalidator : IWidgetIconInvalidator
{
	private readonly IWidgetVariableIndex _variableIndex;
	private readonly WidgetIconEvalChannel _queue;

	public WidgetIconInvalidator(IWidgetVariableIndex variableIndex, WidgetIconEvalChannel queue)
	{
		_variableIndex = variableIndex;
		_queue = queue;
	}

	public void Invalidate(string integrationId, string actionId)
	{
		foreach (var widgetId in _variableIndex.FindIconProviderReferences(integrationId, actionId))
		{
			_queue.Enqueue(widgetId);
		}
	}
}
