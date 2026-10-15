using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.Streaming.Widgets;
using MacroDeckHost.Integrations.Twitch.Widgets;
using Serilog.Core;
using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Tests.UnitTests.Streaming;

internal static class StreamPlatformTestSupport
{
	public static StreamPlatform TestPlatform { get; } = new()
	{
		OwnerId = "app.macro-deck.test-platform",
		DialogViewId = "test-platform-chat",
		AccentColor = "#ff0000",
		AccountLabel = TwitchStreamPlatform.Platform.AccountLabel,
		Chat = TwitchStreamPlatform.Platform.Chat,
		Stats = TwitchStreamPlatform.Platform.Stats
	};

	public static StreamWidgetsUiProvider Provider(
		StreamPlatform platform,
		IStreamChatFeed feed,
		IStreamChatImages? images = null,
		IStreamStatsSource? stats = null,
		IStreamThumbnails? thumbnails = null,
		IIntegrationContext? context = null,
		IStreamChatModerator? moderator = null,
		TimeProvider? time = null)
	{
		return new StreamWidgetsUiProvider(platform,
			feed,
			images,
			stats ?? new StreamStatsSource((_, _) => null),
			thumbnails ?? new NoStreamThumbnails(),
			() => context,
			() => moderator,
			time ?? TimeProvider.System,
			Logger.None);
	}
}

internal sealed class NoStreamThumbnails : IStreamThumbnails
{
	public event EventHandler<string>? Changed
	{
		add { }
		remove { }
	}

	public UiResource? Find(string accountId)
	{
		return null;
	}

	public void Track(string accountId, string? url)
	{
	}
}

internal sealed class StreamingUiIntegration(string id, IUiProvider provider) : IIntegration, IUiProvider
{
	public string Id => id;

	public LocalizedText Name => "Streaming";

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public bool IsInitialized => true;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => provider.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return provider.CreateSessionAsync(request, cancellationToken);
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}
}

internal sealed class FakeStreamingContext : IIntegrationContext
{
	public FakeStreamingContext(string ownerId, Type integrationType, UiResourceStore? store = null)
	{
		Store = store ?? new UiResourceStore();
		UiResources = new BuiltInUiResources(ownerId, Store, integrationType);
	}

	public UiResourceStore Store { get; }

	public IUiResourceRegistry UiResources { get; }

	public FakeStreamingWidgets OwnWidgets { get; } = new();

	public IWidgetApi Widgets => OwnWidgets;

	public FakeStreamingVariables OwnVariables { get; } = new();

	public IVariableApi Variables => OwnVariables;

	public IIntegrationConfig Config { get; init; } = null!;

	public IEventPublisher Events { get; init; } = null!;

	public IUserVariableApi UserVariables { get; init; } = null!;

	public IDeckNavigator Deck => throw new NotSupportedException();

	public IScriptApi Scripts => throw new NotSupportedException();

	public IUserNotifier Notifications => throw new NotSupportedException();
}

internal sealed class FakeStreamingWidgets : IWidgetApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, WidgetDataSnapshot> _data = new(StringComparer.Ordinal);

	public int Reads { get; private set; }

	public void Store(Guid widgetId, string widgetTypeId, string json)
	{
		using var document = JsonDocument.Parse(json);
		lock (_gate)
		{
			_data[widgetId.ToString()] = new WidgetDataSnapshot(widgetId.ToString(),
				widgetTypeId,
				document.RootElement.Clone());
		}
	}

	public void Remove(Guid widgetId)
	{
		lock (_gate)
		{
			_data.Remove(widgetId.ToString());
		}
	}

	public Task<WidgetDataSnapshot?> GetWidgetDataAsync(string widgetId, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			Reads++;
			return Task.FromResult(_data.GetValueOrDefault(widgetId));
		}
	}

	public IReadOnlyList<WidgetTargetInfo> GetWidgets()
	{
		return [];
	}

	public bool Exists(string widgetId)
	{
		return false;
	}

	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(false);
	}
}

internal sealed class FakeStreamingVariables : IVariableApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, List<double?>> _samples = new(StringComparer.Ordinal);
	private readonly List<Window> _windows = [];

	public IReadOnlyList<string> Opened
	{
		get
		{
			lock (_gate)
			{
				return [.. _windows.Select(window => window.Name)];
			}
		}
	}

	public void Sample(string name, double value)
	{
		List<Window> windows;
		lock (_gate)
		{
			if (!_samples.TryGetValue(name, out var samples))
			{
				_samples[name] = samples = [];
			}

			samples.Add(value);
			windows = [.. _windows.Where(window => window.Name == name)];
		}

		foreach (var window in windows)
		{
			window.Raise();
		}
	}

	public Task<IVariableHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		var window = new Window(this, name, capacity);
		lock (_gate)
		{
			_windows.Add(window);
		}

		return Task.FromResult<IVariableHistoryWindow>(window);
	}

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		return Task.FromResult<IReadOnlyList<VariableHandle>>([]);
	}

	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		return Task.FromResult<VariableHandle?>(null);
	}

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		throw new NotSupportedException();
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		throw new NotSupportedException();
	}

	public Task DeleteAsync(Guid variableId)
	{
		return Task.CompletedTask;
	}

	private IReadOnlyList<double?> Values(string name, int capacity)
	{
		lock (_gate)
		{
			return _samples.TryGetValue(name, out var samples) ? [.. samples.TakeLast(capacity)] : [];
		}
	}

	private void Close(Window window)
	{
		lock (_gate)
		{
			_windows.Remove(window);
		}
	}

	private sealed class Window(FakeStreamingVariables owner, string name, int capacity) : IVariableHistoryWindow
	{
		public string Name => name;

		public IReadOnlyList<double?> Values => owner.Values(name, capacity);

		public event EventHandler? Changed;

		public void Raise()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public ValueTask DisposeAsync()
		{
			owner.Close(this);
			return ValueTask.CompletedTask;
		}
	}
}

internal sealed class RecordingEventOrigins
{
	public TaskCompletionSource<(string? Client, ModalDefinition Modal)> Opened { get; }
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	public IUiEventOrigin For(string? clientId)
	{
		return new Origin(this, clientId);
	}

	private sealed class Origin(RecordingEventOrigins owner, string? clientId) : IUiEventOrigin
	{
		public string? ClientId => clientId;

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(clientId is not null);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			owner.Opened.TrySetResult((clientId, modal));
			return Task.FromResult(clientId is not null);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}
}
