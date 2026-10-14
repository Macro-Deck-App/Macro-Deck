using System.Text.Json;
using MacroDeck.Sdk.Notifications;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// One notification shown through <see cref="FakeNotificationService" />. A test plays the user with
/// <see cref="Respond" /> and the host with <see cref="Close" />; every close produces the result exactly
/// once, like the real host.
/// </summary>
public sealed class FakeNotification : INotificationHandle
{
	private readonly Lock _gate = new();

	private readonly TaskCompletionSource<NotificationResult> _completion =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly List<NotificationUpdate> _updates = [];

	internal FakeNotification(NotificationOptions options)
	{
		Options = options;
	}

	public Guid Id { get; } = Guid.NewGuid();

	/// <summary>What the plugin asked to show, as originally passed.</summary>
	public NotificationOptions Options { get; }

	public Task<NotificationResult> Completion => _completion.Task;

	public bool IsOpen => !_completion.Task.IsCompleted;

	/// <summary>Every update the plugin applied, in order.</summary>
	public IReadOnlyList<NotificationUpdate> Updates
	{
		get
		{
			lock (_gate)
			{
				return [.. _updates];
			}
		}
	}

	public Task UpdateAsync(NotificationUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);

		lock (_gate)
		{
			_updates.Add(update);
		}

		return Task.CompletedTask;
	}

	public Task DismissAsync(CancellationToken cancellationToken = default)
	{
		Close(NotificationCloseReason.Programmatic);
		return Task.CompletedTask;
	}

	/// <summary>Presses an action as a user would. Returns false when the notification was already closed.</summary>
	public bool Respond(
		string actionId,
		IReadOnlyDictionary<string, JsonElement>? values = null,
		string clientId = "admin")
	{
		return _completion.TrySetResult(new NotificationResult
		{
			CloseReason = NotificationCloseReason.Action,
			ActionId = actionId,
			ClientId = clientId,
			Values = values ?? new Dictionary<string, JsonElement>()
		});
	}

	/// <summary>Closes the notification the way the host would, for example by timeout.</summary>
	public bool Close(NotificationCloseReason reason)
	{
		return _completion.TrySetResult(new NotificationResult { CloseReason = reason });
	}
}

/// <summary>In-memory <see cref="INotificationService" />. Nothing is validated, nothing times out.</summary>
public sealed class FakeNotificationService : INotificationService
{
	private readonly Lock _gate = new();
	private readonly List<FakeNotification> _shown = [];

	/// <summary>Every notification shown so far, in order, open or closed.</summary>
	public IReadOnlyList<FakeNotification> Shown
	{
		get
		{
			lock (_gate)
			{
				return [.. _shown];
			}
		}
	}

	/// <summary>The notifications that have not closed yet.</summary>
	public IReadOnlyList<FakeNotification> Open => [.. Shown.Where(n => n.IsOpen)];

	public Task<INotificationHandle> ShowAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		var notification = new FakeNotification(options);
		lock (_gate)
		{
			_shown.Add(notification);
		}

		return Task.FromResult<INotificationHandle>(notification);
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
}
