using MacroDeck.Sdk.MusicPlayer;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class InProcessMusicPlayerApi : IMusicPlayerApi, IDisposable
{
	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerAlbumColor _albumColor;
	private readonly IMusicPlayerStateNotifier _notifier;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<EventHandler> _handlers = [];
	private IReadOnlyList<MusicPlayerInstanceSnapshot> _lastRaised = [];
	private Task _raising = Task.CompletedTask;
	private bool _disposed;

	public InProcessMusicPlayerApi(
		IMusicPlayerRegistry registry,
		IMusicPlayerAlbumColor albumColor,
		IMusicPlayerStateNotifier notifier,
		ILogger logger)
	{
		_registry = registry;
		_albumColor = albumColor;
		_notifier = notifier;
		_logger = logger.ForContext<InProcessMusicPlayerApi>();
		_notifier.InstancesChanged += OnInstancesChanged;
	}

	public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances
		=> MusicPlayerInstanceSnapshots.From(_registry.GetInstances());

	public event EventHandler? InstancesChanged
	{
		add
		{
			if (value is null)
			{
				return;
			}

			var current = TryReadInstances() ?? [];
			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				if (_handlers.Count == 0)
				{
					_lastRaised = current;
				}

				_handlers.Add(value);
			}
		}
		remove
		{
			if (value is null)
			{
				return;
			}

			lock (_gate)
			{
				var index = _handlers.LastIndexOf(value);
				if (index >= 0)
				{
					_handlers.RemoveAt(index);
				}
			}
		}
	}

	public Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);
		return _albumColor.GetAsync(instanceId, cancellationToken);
	}

	public void Dispose()
	{
		_notifier.InstancesChanged -= OnInstancesChanged;
		lock (_gate)
		{
			_disposed = true;
			_handlers.Clear();
		}
	}

	// The notifier also fires when only widget options changed, which a reader of this api cannot see.
	private void OnInstancesChanged(object? sender, EventArgs args)
	{
		lock (_gate)
		{
			if (_disposed || _handlers.Count == 0)
			{
				return;
			}
		}

		if (TryReadInstances() is not { } current)
		{
			return;
		}

		lock (_gate)
		{
			if (_disposed || _handlers.Count == 0 || _lastRaised.SequenceEqual(current))
			{
				return;
			}

			_lastRaised = current;
			_raising = _raising.ContinueWith(_ => Raise(),
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private IReadOnlyList<MusicPlayerInstanceSnapshot>? TryReadInstances()
	{
		try
		{
			return Instances;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not list the music players");
			return null;
		}
	}

	private void Raise()
	{
		EventHandler[] handlers;
		lock (_gate)
		{
			handlers = [.. _handlers];
		}

		foreach (var handler in handlers)
		{
			try
			{
				handler(this, EventArgs.Empty);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A music player InstancesChanged handler failed");
			}
		}
	}
}

public static class MusicPlayerInstanceSnapshots
{
	public static IReadOnlyList<MusicPlayerInstanceSnapshot> From(
		IReadOnlyList<MusicPlayerInstanceDescriptor> descriptors)
	{
		return [.. descriptors.Select(From)];
	}

	public static MusicPlayerInstanceSnapshot From(MusicPlayerInstanceDescriptor descriptor)
	{
		return new MusicPlayerInstanceSnapshot
		{
			Id = descriptor.InstanceId,
			ProviderId = descriptor.IntegrationId,
			ProviderName = descriptor.ProviderName,
			DisplayName = descriptor.DisplayName
		};
	}
}
