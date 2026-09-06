using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Services;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Scripts;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Scripts;

public sealed class ScriptApi : IScriptApi
{
	private readonly IServiceScopeFactory _scopeFactory;

	public ScriptApi(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public IReadOnlyList<Script> GetScripts()
	{
		using var scope = _scopeFactory.CreateScope();
		return scope.ServiceProvider.GetRequiredService<IScriptService>()
			.GetAll()
			.Select(script => new Script
			{
				Id = script.Id.ToString(),
				Name = script.Name,
				Description = script.Description,
				Inputs = script.Inputs.Select(ScriptInputSdkMapper.ToSdk).ToList(),
				RunsOnWidget = script.RunsOnWidget
			})
			.ToList();
	}

	public async Task<ActionResult> RunAsync(
		string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(scriptId, out var id))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, "The script id is not valid.");
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var result = await scope.ServiceProvider.GetRequiredService<IScriptRunner>()
			.RunAsync(id, originClientId, cancellationToken, 0, inputs, ownerWidgetId);

		return result.Status == FlowExecutionStatus.Succeeded
			? ActionResult.Success()
			: ActionResult.Failed(result.ErrorCode ?? ActionExecutionErrorCodes.FlowError,
				result.ErrorMessage.IsEmpty ? "The script failed." : result.ErrorMessage);
	}
}
