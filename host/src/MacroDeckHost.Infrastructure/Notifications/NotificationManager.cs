using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Notifications;

public sealed class NotificationManager : INotificationManager, IUserNotificationStore, IDisposable
{
	private const int RecentlyClosedCapacity = 256;

	private readonly object _lock = new();
	private readonly Dictionary<Guid, Popup> _popups = [];
	private readonly Dictionary<string, RateBucket> _createBuckets = new(StringComparer.Ordinal);
	private readonly Dictionary<string, RateBucket> _updateBuckets = new(StringComparer.Ordinal);
	private readonly HashSet<Guid> _recentlyClosed = [];
	private readonly Queue<Guid> _recentlyClosedOrder = new();
	private readonly UserNotificationStore _inbox;
	private readonly ILocalizationResolver _localization;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public NotificationManager(
		UserNotificationStore inbox,
		ILocalizationResolver localization,
		IServiceScopeFactory scopeFactory,
		TimeProvider time,
		ILogger logger)
	{
		_inbox = inbox;
		_localization = localization;
		_scopeFactory = scopeFactory;
		_time = time;
		_logger = logger.ForContext<NotificationManager>();
	}

	public event Action<NotificationEvent>? Published;

	public int Capacity => _inbox.Capacity;

	public event Action? Changed
	{
		add => _inbox.Changed += value;
		remove => _inbox.Changed -= value;
	}

	public UserNotification? Raise(UserNotificationDraft draft)
	{
		return _inbox.Raise(draft);
	}

	public UserNotification? RaiseIfAbsent(UserNotificationDraft draft)
	{
		return _inbox.RaiseIfAbsent(draft);
	}

	public UserNotification? RaiseIfPresent(UserNotificationDraft draft)
	{
		return _inbox.RaiseIfPresent(draft);
	}

	public IReadOnlyList<UserNotification> Snapshot()
	{
		return _inbox.Snapshot();
	}

	public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress)
	{
		return _inbox.UpdateProgress(dedupeKey, progress);
	}

	public bool Dismiss(string id)
	{
		return _inbox.Dismiss(id);
	}

	public bool DismissByKey(string dedupeKey)
	{
		return _inbox.DismissByKey(dedupeKey);
	}

	public bool Retire(string dedupeKey)
	{
		return _inbox.Retire(dedupeKey);
	}

	public bool DismissAll()
	{
		return _inbox.DismissAll();
	}

	public bool MarkRead(string id)
	{
		return _inbox.MarkRead(id);
	}

	public bool MarkAllRead()
	{
		return _inbox.MarkAllRead();
	}

	public async Task<NotificationShowOutcome> ShowAsync(
		NotificationOwner owner,
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(options);

		var content = Validate(options);
		var culture = await CultureAsync().ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_lock)
		{
			var now = _time.GetUtcNow();

			if (!Bucket(_createBuckets,
					owner.Id,
					NotificationLimits.CreatesPerMinutePerOwner,
					NotificationLimits.CreatesPerMinutePerOwner / 60.0)
				.TryConsume(now))
			{
				throw new NotificationException(NotificationErrorCode.LimitExceeded,
					"This owner is showing notifications too quickly.");
			}

			var id = Guid.NewGuid();
			var lifetime = Lifetime(options);

			if (options.Popup == NotificationPopup.None && options.Progress is null)
			{
				var entry = new Popup
				{
					Id = id,
					Owner = owner,
					Options = options,
					Content = null,
					CreatedAt = now,
					Lifetime = lifetime,
					ExpiresAt = now + lifetime,
					Completion = new TaskCompletionSource<NotificationResult>(TaskCreationOptions
						.RunContinuationsAsynchronously)
				};

				RaiseInbox(entry, culture, false);
				var done = new NotificationResult { CloseReason = NotificationCloseReason.Programmatic };
				entry.Completion.TrySetResult(done);
				Publish(new NotificationEvent(NotificationEventKind.Closed, View(entry), done));
				return new NotificationShowOutcome(id, entry.Completion.Task);
			}

			EnsureRoom(owner, content?.SizeBytes ?? 0);
			var popup = new Popup
			{
				Id = id,
				Owner = owner,
				Options = options,
				Content = content,
				CreatedAt = now,
				Lifetime = lifetime,
				ExpiresAt = now + lifetime,
				Completion = new TaskCompletionSource<NotificationResult>(TaskCreationOptions
					.RunContinuationsAsynchronously)
			};

			_popups[id] = popup;
			popup.Timer = _time.CreateTimer(_ => CloseInternal(id, NotificationCloseReason.Timeout, null),
				null,
				lifetime,
				Timeout.InfiniteTimeSpan);

			if (options.Inbox)
			{
				RaiseInbox(popup, culture, false);
			}

			Publish(new NotificationEvent(NotificationEventKind.Shown, View(popup)));

			return new NotificationShowOutcome(id, popup.Completion.Task);
		}
	}

	public async Task UpdateAsync(
		NotificationOwner owner,
		Guid id,
		NotificationUpdate update,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(update);

		var culture = await CultureAsync().ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_lock)
		{
			var popup = Owned(owner, id);

			if (!Bucket(_updateBuckets,
					owner.Id,
					NotificationLimits.UpdatesPerSecondPerOwner,
					NotificationLimits.UpdatesPerSecondPerOwner)
				.TryConsume(_time.GetUtcNow()))
			{
				throw new NotificationException(NotificationErrorCode.LimitExceeded,
					"This owner is updating notifications too quickly.");
			}

			var options = popup.Options;
			var content = popup.Content;
			var contentChanged = false;

			if (update.Title is { } title)
			{
				ValidateTitle(title);
				options = options with { Title = title };
			}

			if (update.Message is { } message)
			{
				ValidateMessage(message);
				options = options with { Message = message };
			}

			if (update.Severity is { } severity)
			{
				options = options with { Severity = severity };
			}

			if (update.Actions is { } actions)
			{
				ValidateActions(actions);
				options = options with { Actions = actions };
			}

			if (update.Content is { } updatedContent)
			{
				if (options.Popup != NotificationPopup.Dialog)
				{
					throw Invalid("Only a dialog can carry content.");
				}

				var inspected = NotificationContent.Inspect(updatedContent);
				EnsureRoom(popup.Owner, inspected.SizeBytes - (popup.Content?.SizeBytes ?? 0), popup);
				content = inspected;
				options = options with { Content = updatedContent };
				contentChanged = true;
			}

			var progressOnly = update.Title is null
				&& update.Message is null
				&& update.Severity is null
				&& update.Actions is null
				&& !contentChanged
				&& update.Progress is not null;

			if (update.Progress is { } progress)
			{
				options = options with { Progress = progress };
			}

			EnsureClosable(options);
			if (update.Progress is { } updatedProgress)
			{
				ValidateProgress(updatedProgress);
			}

			var now = _time.GetUtcNow();
			popup.ExpiresAt = Min(now + popup.Lifetime, popup.CreatedAt + NotificationLimits.MaxLifetime);
			popup.Timer?.Change(popup.ExpiresAt - now, Timeout.InfiniteTimeSpan);
			popup.Options = options;
			popup.Content = content;
			popup.Revision++;

			if (options.Inbox)
			{
				RaiseInbox(popup, culture, progressOnly, true);
			}

			Publish(new NotificationEvent(NotificationEventKind.Updated, View(popup)));
		}
	}

	public void Close(NotificationOwner owner, Guid id)
	{
		ArgumentNullException.ThrowIfNull(owner);

		lock (_lock)
		{
			if (!_popups.TryGetValue(id, out var popup))
			{
				return;
			}

			if (!SameOwner(popup.Owner, owner))
			{
				throw new NotificationException(NotificationErrorCode.Forbidden,
					"This notification belongs to another owner.");
			}
		}

		CloseInternal(id, NotificationCloseReason.Programmatic, null);
	}

	public NotificationRespondOutcome Respond(
		NotificationPrincipal principal,
		Guid id,
		string? actionId,
		IReadOnlyDictionary<string, JsonElement>? values)
	{
		ArgumentNullException.ThrowIfNull(principal);

		lock (_lock)
		{
			if (!_popups.TryGetValue(id, out var popup) || popup.Options.Popup == NotificationPopup.None)
			{
				return new NotificationRespondOutcome(_recentlyClosed.Contains(id)
					? NotificationRespondStatus.AlreadyClosed
					: NotificationRespondStatus.NotFound);
			}

			if (!Targets(popup.Options.Target, principal))
			{
				return new NotificationRespondOutcome(NotificationRespondStatus.Forbidden);
			}

			NotificationResult result;
			if (actionId is null)
			{
				if (!popup.Options.Dismissible)
				{
					return new NotificationRespondOutcome(NotificationRespondStatus.Invalid,
						"This notification cannot be dismissed.");
				}

				result = new NotificationResult
				{
					CloseReason = NotificationCloseReason.Dismissed, ClientId = principal.ClientId
				};
			}
			else
			{
				if (!popup.Options.Actions.Any(a => string.Equals(a.Id, actionId, StringComparison.Ordinal)))
				{
					return new NotificationRespondOutcome(NotificationRespondStatus.Invalid,
						"The action is not part of this notification.");
				}

				Dictionary<string, JsonElement> accepted = [];
				if (popup.Content is { } info
					&& !NotificationContent.TryValidateValues(info.Fields, values, out accepted, out var error))
				{
					return new NotificationRespondOutcome(NotificationRespondStatus.Invalid, error);
				}

				result = new NotificationResult
				{
					CloseReason = NotificationCloseReason.Action,
					ActionId = actionId,
					ClientId = principal.ClientId,
					Values = accepted
				};
			}

			CloseInternal(id, result.CloseReason, result);
			return new NotificationRespondOutcome(NotificationRespondStatus.Accepted);
		}
	}

	public IReadOnlyList<ActiveNotification> Snapshot(NotificationPrincipal principal)
	{
		ArgumentNullException.ThrowIfNull(principal);

		lock (_lock)
		{
			return
			[
				.. _popups.Values
					.Where(p => p.Options.Popup != NotificationPopup.None && Targets(p.Options.Target, principal))
					.OrderBy(p => p.CreatedAt)
					.Select(View)
			];
		}
	}

	public ActiveNotification? Find(Guid id)
	{
		lock (_lock)
		{
			return _popups.TryGetValue(id, out var popup) ? View(popup) : null;
		}
	}

	public int CloseOwner(string ownerId, NotificationCloseReason reason = NotificationCloseReason.OwnerGone)
	{
		List<Guid> ids;
		lock (_lock)
		{
			ids =
			[
				.. _popups.Values.Where(p => string.Equals(p.Owner.Id, ownerId, StringComparison.Ordinal))
					.Select(p => p.Id)
			];
		}

		return ids.Count(id => CloseInternal(id, reason, null));
	}

	public void Dispose()
	{
		lock (_lock)
		{
			foreach (var popup in _popups.Values)
			{
				popup.Timer?.Dispose();
			}
		}
	}

	private bool CloseInternal(Guid id, NotificationCloseReason reason, NotificationResult? result)
	{
		lock (_lock)
		{
			if (!_popups.Remove(id, out var popup))
			{
				return false;
			}

			popup.Timer?.Dispose();
			popup.Revision++;
			Remember(id);

			result ??= new NotificationResult { CloseReason = reason };

			if (popup.Options.Inbox && popup.Options.Progress is not null)
			{
				popup.Options = popup.Options with { Progress = null };
				RaiseInbox(popup, null, false, true);
			}

			popup.Completion.TrySetResult(result);
			Publish(new NotificationEvent(NotificationEventKind.Closed, View(popup), result));
			return true;
		}
	}

	private void Remember(Guid id)
	{
		_recentlyClosed.Add(id);
		_recentlyClosedOrder.Enqueue(id);

		while (_recentlyClosedOrder.Count > RecentlyClosedCapacity)
		{
			_recentlyClosed.Remove(_recentlyClosedOrder.Dequeue());
		}
	}

	private Popup Owned(NotificationOwner owner, Guid id)
	{
		if (!_popups.TryGetValue(id, out var popup))
		{
			throw new NotificationException(NotificationErrorCode.NotFound, "The notification is not open.");
		}

		if (!SameOwner(popup.Owner, owner))
		{
			throw new NotificationException(NotificationErrorCode.Forbidden,
				"This notification belongs to another owner.");
		}

		return popup;
	}

	private static bool SameOwner(NotificationOwner a, NotificationOwner b)
	{
		return a.Category == b.Category && string.Equals(a.Id, b.Id, StringComparison.Ordinal);
	}

	private static bool Targets(NotificationTarget target, NotificationPrincipal principal)
	{
		return target.Kind switch
		{
			NotificationTargetKind.All => principal.IsAdmin || principal.DeviceId is not null,
			NotificationTargetKind.AdminUi => principal.IsAdmin,
			NotificationTargetKind.Devices => principal.DeviceId is { } device && target.DeviceIds.Contains(device),
			_ => false
		};
	}

	private ActiveNotification View(Popup popup)
	{
		var options = popup.Options;

		return new ActiveNotification
		{
			Id = popup.Id,
			Revision = popup.Revision,
			Owner = popup.Owner,
			Title = options.Title,
			Message = options.Message,
			Severity = options.Severity,
			Popup = options.Popup,
			Inbox = options.Inbox,
			Dismissible = options.Dismissible,
			Target = options.Target,
			CreatedAt = popup.CreatedAt,
			ExpiresAt = popup.ExpiresAt,
			Actions = options.Actions,
			Content = options.Content,
			Progress = options.Progress
		};
	}

	private static TimeSpan Lifetime(NotificationOptions options)
	{
		var requested = options.Duration
			?? (options.Popup == NotificationPopup.Toast
				? NotificationLimits.DefaultToastDuration
				: NotificationLimits.MaxLifetime);

		return requested < NotificationLimits.MinDuration ? NotificationLimits.MinDuration
			: requested > NotificationLimits.MaxLifetime ? NotificationLimits.MaxLifetime
			: requested;
	}

	private void EnsureRoom(NotificationOwner owner, int additionalContentBytes, Popup? replacing = null)
	{
		var ownerCount = 0;
		var ownerBytes = 0;
		var totalBytes = 0;

		foreach (var popup in _popups.Values)
		{
			var bytes = popup.Content?.SizeBytes ?? 0;
			totalBytes += bytes;

			if (string.Equals(popup.Owner.Id, owner.Id, StringComparison.Ordinal))
			{
				ownerCount++;
				ownerBytes += bytes;
			}
		}

		if (replacing is null
			&& (_popups.Count >= NotificationLimits.MaxActive || ownerCount >= NotificationLimits.MaxActivePerOwner))
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				"Too many notifications are open.");
		}

		var isSystem = owner.Category == NotificationSourceCategory.System;
		var globalBudget = isSystem
			? NotificationLimits.ContentBudgetGlobalBytes
			: NotificationLimits.ContentBudgetGlobalBytes - NotificationLimits.ContentBudgetSystemReserveBytes;

		if (additionalContentBytes > 0
			&& (ownerBytes + additionalContentBytes > NotificationLimits.ContentBudgetPerOwnerBytes
				|| totalBytes + additionalContentBytes > globalBudget))
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				"The open notifications carry too much content.");
		}
	}

	private static NotificationContentInfo? Validate(NotificationOptions options)
	{
		ValidateTitle(options.Title);

		if (options.Message is { } message)
		{
			ValidateMessage(message);
		}

		if (options.Popup == NotificationPopup.None && !options.Inbox)
		{
			throw Invalid("A notification needs a popup or an Inbox entry.");
		}

		EnsureClosable(options);

		if (options.Progress is { } progress)
		{
			ValidateProgress(progress);
		}

		if (options.Duration is { } duration && duration <= TimeSpan.Zero)
		{
			throw Invalid("The duration must be positive.");
		}

		if (options.Target.Kind == NotificationTargetKind.Devices && options.Target.DeviceIds.Count == 0)
		{
			throw Invalid("A device target needs at least one device id.");
		}

		if (options.Target.DeviceIds.Count > 50)
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded, "Too many target devices.");
		}

		ValidateActions(options.Actions);

		if (options.Content is null)
		{
			return null;
		}

		if (options.Popup != NotificationPopup.Dialog)
		{
			throw Invalid("Only a dialog can carry content.");
		}

		return NotificationContent.Inspect(options.Content);
	}

	private static void EnsureClosable(NotificationOptions options)
	{
		if (options.Popup == NotificationPopup.Dialog
			&& options.Actions.Count == 0
			&& !options.Dismissible
			&& options.Duration is null)
		{
			throw Invalid("A dialog needs an action, a way to dismiss it or a duration.");
		}
	}

	private static void ValidateProgress(NotificationProgress progress)
	{
		if (progress.Processed < 0 || progress.Total is < 1)
		{
			throw Invalid("The progress needs a processed count of zero or more and a total of one or more.");
		}
	}

	private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b)
	{
		return a < b ? a : b;
	}

	private static void ValidateTitle(LocalizedText title)
	{
		if (title.IsEmpty || title.Literal is { } literal && string.IsNullOrWhiteSpace(literal))
		{
			throw Invalid("A notification needs a title.");
		}

		if (title.Literal is { Length: > NotificationLimits.MaxTitleLength })
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded, "The title is too long.");
		}
	}

	private static void ValidateMessage(LocalizedText message)
	{
		if (message.Literal is { Length: > NotificationLimits.MaxMessageLength })
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded, "The message is too long.");
		}
	}

	private static void ValidateActions(IReadOnlyList<NotificationAction> actions)
	{
		if (actions.Count > NotificationLimits.MaxActions)
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				$"A notification has at most {NotificationLimits.MaxActions} actions.");
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var action in actions)
		{
			if (string.IsNullOrWhiteSpace(action.Id)
				|| action.Id.Length > NotificationLimits.MaxActionIdLength
				|| !seen.Add(action.Id))
			{
				throw Invalid("Action ids must be unique, not empty and short.");
			}

			if (action.Label.IsEmpty)
			{
				throw Invalid("Every action needs a label.");
			}
		}
	}

	private async Task<string?> CultureAsync()
	{
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
			return (await preferences.GetLocalization().ConfigureAwait(false)).Culture;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "The localization settings could not be read for a notification");
			return null;
		}
	}

	private void RaiseInbox(Popup popup, string? culture, bool progressOnly, bool replaceOnly = false)
	{
		var options = popup.Options;
		var key = $"popup:{popup.Id}";

		if (progressOnly
			&& options.Progress is { } progress
			&& _inbox.UpdateProgress(key, new UserNotificationProgress(progress.Processed, progress.Total)))
		{
			return;
		}

		culture ??= popup.InboxCulture;
		popup.InboxCulture = culture;

		var owner = popup.Owner;
		var hasIntegration = owner.Category != NotificationSourceCategory.System;

		var draft = new UserNotificationDraft
		{
			Id = popup.Id.ToString(),
			DedupeKey = key,
			Severity = options.Severity switch
			{
				NotificationSeverity.Success => UserNotificationSeverity.Success,
				NotificationSeverity.Warning => UserNotificationSeverity.Warning,
				NotificationSeverity.Error => UserNotificationSeverity.Error,
				_ => UserNotificationSeverity.Info
			},
			Kind = hasIntegration ? UserNotificationKind.Integration : UserNotificationKind.General,
			Title = _localization.Resolve(options.Title, culture) ?? string.Empty,
			Message = options.Message is { } message ? _localization.Resolve(message, culture) : null,
			SourceId = owner.Id,
			SourceName = owner.Name,
			SourceCategory = owner.Category,
			Action = hasIntegration
				? new UserNotificationAction(UserNotificationActionKind.OpenIntegration, owner.Id)
				: null,
			Progress = options.Progress is { } p ? new UserNotificationProgress(p.Processed, p.Total) : null
		};

		if (replaceOnly)
		{
			_inbox.RaiseIfPresent(draft);
		}
		else
		{
			_inbox.Raise(draft);
		}
	}

	private void Publish(NotificationEvent notification)
	{
		try
		{
			Published?.Invoke(notification);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "A notification subscriber failed");
		}
	}

	private RateBucket Bucket(Dictionary<string, RateBucket> buckets, string ownerId, double capacity, double perSecond)
	{
		if (!buckets.TryGetValue(ownerId, out var bucket))
		{
			bucket = new RateBucket(capacity, perSecond, _time.GetUtcNow());
			buckets[ownerId] = bucket;
		}

		return bucket;
	}

	private static NotificationException Invalid(string message)
	{
		return new NotificationException(NotificationErrorCode.Invalid, message);
	}

	private sealed class Popup
	{
		public required Guid Id { get; init; }

		public required NotificationOwner Owner { get; init; }

		public required NotificationOptions Options { get; set; }

		public required NotificationContentInfo? Content { get; set; }

		public required DateTimeOffset CreatedAt { get; init; }

		public required TimeSpan Lifetime { get; init; }

		public required DateTimeOffset ExpiresAt { get; set; }

		public required TaskCompletionSource<NotificationResult> Completion { get; init; }

		public long Revision { get; set; }

		public ITimer? Timer { get; set; }

		public string? InboxCulture { get; set; }
	}

	private sealed class RateBucket
	{
		private readonly double _capacity;
		private readonly double _perSecond;
		private double _tokens;
		private DateTimeOffset _last;

		public RateBucket(double capacity, double perSecond, DateTimeOffset now)
		{
			_capacity = capacity;
			_perSecond = perSecond;
			_tokens = capacity;
			_last = now;
		}

		public bool TryConsume(DateTimeOffset at)
		{
			var elapsed = (at - _last).TotalSeconds;
			if (elapsed > 0)
			{
				_tokens = Math.Min(_capacity, _tokens + elapsed * _perSecond);
				_last = at;
			}

			if (_tokens < 1)
			{
				return false;
			}

			_tokens -= 1;
			return true;
		}
	}
}
