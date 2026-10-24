using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Windows.AutoHotkey;

internal sealed class MemoryStorage : IIntegrationStorage
{
	private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

	public Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_documents.TryGetValue(key, out var content)
			? IntegrationStorageReadResult.Found(content)
			: IntegrationStorageReadResult.Missing);
	}

	public Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		_documents[key] = content;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		_documents.Remove(key);
		return Task.CompletedTask;
	}
}

internal sealed class MemoryUserVariables : IUserVariableApi
{
	private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

	public string? this[string name]
	{
		get
		{
			lock (_values)
			{
				return _values.GetValueOrDefault(name);
			}
		}
		set
		{
			lock (_values)
			{
				_values[name] = value ?? string.Empty;
			}
		}
	}

	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		this[name] = value;
		return Task.FromResult(UserVariableWriteResult.Applied());
	}

	public Task<UserVariableReadResult> ReadInContextAsync(
		string name,
		UserVariableContext context,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(this[name] is { } value
			? UserVariableReadResult.Found(VariableType.Text, value, value)
			: UserVariableReadResult.Failed(UserVariableReadStatus.NotFound, "not found"));
	}
}

internal sealed class MemoryNotifier : IUserNotifier
{
	public List<UserNotificationRequest> Notifications { get; } = [];

	public void Notify(UserNotificationRequest notification)
	{
		lock (Notifications)
		{
			Notifications.Add(notification);
		}
	}

	public void Dismiss(string key)
	{
	}
}
