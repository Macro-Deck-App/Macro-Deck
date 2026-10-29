using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Mcp;

public sealed class McpGuardedUserVariableApi : IUserVariableApi
{
	private readonly string _integrationId;
	private readonly IUserVariableApi _inner;
	private readonly IMcpVariableWriteGuard _guard;

	public McpGuardedUserVariableApi(string integrationId, IUserVariableApi inner, IMcpVariableWriteGuard guard)
	{
		_integrationId = integrationId;
		_inner = inner;
		_guard = guard;
	}

	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		return ApplyInContextAsync(name, new UserVariableContext(ownerWidgetId), operation, value, cancellationToken);
	}

	public Task<UserVariableWriteResult> ApplyInContextAsync(
		string name,
		UserVariableContext context,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		return Refused(_guard, _integrationId, name) is { } refusal
			? Task.FromResult(refusal)
			: _inner.ApplyInContextAsync(name, context, operation, value, cancellationToken);
	}

	public Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		return CreateInContextAsync(name,
			new UserVariableContext(ownerWidgetId),
			type,
			initialValue,
			decimalPlaces,
			cancellationToken);
	}

	public Task<UserVariableCreateResult> CreateInContextAsync(
		string name,
		UserVariableContext context,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		return _guard.Refuses(_integrationId, name)
			? Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.NotSupported,
				McpVariableWriteGuard.RefusalMessage))
			: _inner.CreateInContextAsync(name, context, type, initialValue, decimalPlaces, cancellationToken);
	}

	public Task<UserVariableReadResult> ReadInContextAsync(
		string name,
		UserVariableContext context,
		CancellationToken cancellationToken = default)
	{
		return _inner.ReadInContextAsync(name, context, cancellationToken);
	}

	public static UserVariableWriteResult? Refused(IMcpVariableWriteGuard guard, string integrationId, string name)
	{
		return guard.Refuses(integrationId, name)
			? UserVariableWriteResult.Failed(UserVariableWriteStatus.NotEditable, McpVariableWriteGuard.RefusalMessage)
			: null;
	}
}
