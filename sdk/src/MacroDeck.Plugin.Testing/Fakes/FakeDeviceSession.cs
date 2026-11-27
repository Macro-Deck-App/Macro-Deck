using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// An <see cref="IDeviceSession" /> with no host behind it, for testing an <see cref="IDeviceProvider" />
/// in isolation: the test pushes the surfaces the provider should render and reads back the interactions
/// it reported. The host's own verdicts are stubbed rather than derived - a rejection and an unsupported
/// kind are set through <see cref="NextResult" />, because deciding between them is the host's job and
/// re-deriving it here would be a second implementation to keep in step.
/// </summary>
public sealed class FakeDeviceSession : IDeviceSession
{
	private readonly FakeDeviceProviderContext? _context;

	private int _closed;

	public FakeDeviceSession(string deviceId, string providerDeviceId, FakeDeviceProviderContext? context = null)
	{
		DeviceId = deviceId;
		ProviderDeviceId = providerDeviceId;
		_context = context;
	}

	/// <inheritdoc />
	public string DeviceId { get; }

	/// <inheritdoc />
	public string ProviderDeviceId { get; }

	/// <inheritdoc />
	public DeviceSurface CurrentSurface { get; private set; } = DeviceSurface.Empty;

	/// <inheritdoc />
	public event EventHandler<DeviceSurfaceChangedEventArgs>? SurfaceChanged;

	/// <inheritdoc />
	public event EventHandler<DeviceSessionClosedEventArgs>? Closed;

	/// <inheritdoc />
	public event EventHandler<DeviceKeyImageChangedEventArgs>? KeyImageChanged;

	/// <summary>
	/// The format the host renders key pictures in for this session; null, the default, is a session with
	/// no key images. Set it before the provider reads it - a real session's format never changes once it
	/// is open. <see cref="FakeDeviceProviderContext.KeyImageFormat" /> sets it for the sessions a context
	/// opens.
	/// </summary>
	public LayoutKeyImageFormat? KeyImageFormat { get; set; }

	/// <summary>The current picture of each cell, keyed by (row, column), which
	/// <see cref="GetKeyImageAsync" /> answers from. Seed it for a provider that paints every key on open;
	/// <see cref="PushKeyImage" /> keeps it current.</summary>
	public Dictionary<(int Row, int Column), DeviceKeyImage> KeyImages { get; } = new();

	/// <summary>Every key picture the provider asked for, as (row, column).</summary>
	public IReadOnlyList<(int Row, int Column)> KeyImageRequests => _keyImageRequests;

	private readonly List<(int Row, int Column)> _keyImageRequests = [];

	/// <summary>Every interaction the provider reported, in order.</summary>
	public IReadOnlyList<DeviceInteraction> Interactions => _interactions;

	private readonly List<DeviceInteraction> _interactions = [];

	/// <summary>Every icon the provider asked for, as (icon id, size, known ETag).</summary>
	public IReadOnlyList<(string IconId, int? Size, string? KnownETag)> IconRequests => _iconRequests;

	private readonly List<(string IconId, int? Size, string? KnownETag)> _iconRequests = [];

	/// <summary>The verdict the next reported interaction is answered with.</summary>
	public DeviceInteractionResult NextResult { get; set; } = DeviceInteractionResult.Accepted;

	/// <summary>The icons this session can serve, keyed by icon id. An id that is not here answers null,
	/// the same as a host with no such icon.</summary>
	public Dictionary<string, DeviceIconImage> Icons { get; } = new(StringComparer.Ordinal);

	/// <summary>Every provider-owned widget icon the provider asked for, as (widget id, known ETag).</summary>
	public IReadOnlyList<(string WidgetId, string? KnownETag)> WidgetIconRequests => _widgetIconRequests;

	private readonly List<(string WidgetId, string? KnownETag)> _widgetIconRequests = [];

	/// <summary>The provider-owned icons this session can serve, keyed by widget id. A widget that is not
	/// here answers null, the same as a widget with no active icon provider.</summary>
	public Dictionary<string, DeviceWidgetIconImage> WidgetIcons { get; } = new(StringComparer.Ordinal);

	/// <summary>True once the provider disposed the session.</summary>
	public bool IsDisposed { get; private set; }

	/// <summary>Pushes a surface, exactly as the host does, raising <see cref="SurfaceChanged" />.</summary>
	public void PushSurface(DeviceSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		CurrentSurface = surface;
		SurfaceChanged?.Invoke(this, new DeviceSurfaceChangedEventArgs(surface));
	}

	/// <summary>
	/// Pushes one cell's picture, exactly as the host does, raising <see cref="KeyImageChanged" /> and
	/// making it the answer <see cref="GetKeyImageAsync" /> gives for that cell. Nothing is dropped here:
	/// a test pushing a stale revision sees how the provider itself handles it.
	/// </summary>
	public void PushKeyImage(DeviceKeyImage image)
	{
		ArgumentNullException.ThrowIfNull(image);

		KeyImages[(image.Row, image.Column)] = image;
		KeyImageChanged?.Invoke(this, new DeviceKeyImageChangedEventArgs(image));
	}

	/// <summary>Ends the session. Raised once however the close was reached, matching a real session.</summary>
	public void Close(string? reason = null)
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0)
		{
			return;
		}

		Closed?.Invoke(this, new DeviceSessionClosedEventArgs(reason));
	}

	/// <inheritdoc />
	public Task<DeviceInteractionResult> SendInteractionAsync(
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(interaction);

		lock (_interactions)
		{
			_interactions.Add(interaction);
		}

		_context?.RecordInteraction(ProviderDeviceId, interaction);
		return Task.FromResult(NextResult);
	}

	/// <inheritdoc />
	public Task<DeviceIconImage?> GetIconAsync(
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		lock (_iconRequests)
		{
			_iconRequests.Add((iconId, size, knownETag));
		}

		if (!Icons.TryGetValue(iconId, out var icon))
		{
			return Task.FromResult<DeviceIconImage?>(null);
		}

		return Task.FromResult<DeviceIconImage?>(
			knownETag is { Length: > 0 } && string.Equals(knownETag, icon.ETag, StringComparison.Ordinal)
				? icon with { Content = ReadOnlyMemory<byte>.Empty, NotModified = true }
				: icon);
	}

	/// <inheritdoc />
	public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		lock (_widgetIconRequests)
		{
			_widgetIconRequests.Add((widgetId, knownETag));
		}

		if (!WidgetIcons.TryGetValue(widgetId, out var icon))
		{
			return Task.FromResult<DeviceWidgetIconImage?>(null);
		}

		return Task.FromResult<DeviceWidgetIconImage?>(
			knownETag is { Length: > 0 } && string.Equals(knownETag, icon.ETag, StringComparison.Ordinal)
				? icon with { Content = ReadOnlyMemory<byte>.Empty, NotModified = true }
				: icon);
	}

	/// <summary>Answers from <see cref="KeyImages" />, and null while <see cref="KeyImageFormat" /> is null,
	/// as a host that renders no pictures for the session does.</summary>
	public Task<DeviceKeyImage?> GetKeyImageAsync(int row, int column, CancellationToken cancellationToken = default)
	{
		lock (_keyImageRequests)
		{
			_keyImageRequests.Add((row, column));
		}

		return Task.FromResult<DeviceKeyImage?>(KeyImageFormat is null
			? null
			: KeyImages.GetValueOrDefault((row, column)));
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		Close();
		return ValueTask.CompletedTask;
	}
}
