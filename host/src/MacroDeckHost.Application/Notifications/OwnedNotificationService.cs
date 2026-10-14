using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Application.Notifications;

public sealed class OwnedNotificationService : INotificationService
{
	private readonly NotificationOwner _owner;
	private readonly INotificationManager _manager;

	public OwnedNotificationService(NotificationOwner owner, INotificationManager manager)
	{
		_owner = owner;
		_manager = manager;
	}

	public async Task<INotificationHandle> ShowAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		var outcome = await _manager.ShowAsync(_owner, options, cancellationToken).ConfigureAwait(false);
		return new Handle(_owner, _manager, outcome);
	}

	public async Task<NotificationResult> ShowForResultAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		var handle = await ShowAsync(options, cancellationToken).ConfigureAwait(false);

		try
		{
			return await handle.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await handle.DismissAsync(CancellationToken.None).ConfigureAwait(false);
			throw;
		}
	}

	private sealed class Handle(NotificationOwner owner, INotificationManager manager, NotificationShowOutcome outcome)
		: INotificationHandle
	{
		public Guid Id => outcome.Id;

		public Task<NotificationResult> Completion => outcome.Completion;

		public Task UpdateAsync(NotificationUpdate update, CancellationToken cancellationToken = default)
		{
			return manager.UpdateAsync(owner, outcome.Id, update, cancellationToken);
		}

		public Task DismissAsync(CancellationToken cancellationToken = default)
		{
			manager.Close(owner, outcome.Id);
			return Task.CompletedTask;
		}
	}
}
