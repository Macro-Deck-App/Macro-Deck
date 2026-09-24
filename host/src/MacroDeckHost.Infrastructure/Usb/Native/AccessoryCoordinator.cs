using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Usb;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed record AccessoryReconnectPolicy(IReadOnlyList<TimeSpan> ReSwitchDelays, bool ReSwitchAfterBye)
{
	public static readonly AccessoryReconnectPolicy Default = new(
		[TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)],
		ReSwitchAfterBye: false);
}

internal sealed record AccessoryPollContext(
	bool BridgeAvailable,
	IReadOnlySet<string> AdbSerials,
	IReadOnlySet<string> RememberedSerials);

internal sealed class AccessoryCoordinator : INativeUsbSerials
{
	public const int UnplugPolls = 2;

	public static readonly TimeSpan ReopenBackoff = TimeSpan.FromSeconds(30);

	private readonly IUsbHost _usb;
	private readonly Func<IUsbBulkPipe, string, INativeLinkSession> _startSession;
	private readonly AccessoryReconnectPolicy _policy;
	private readonly IReadOnlyList<string> _identification;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<UsbPlugKey, Plug> _plugs = [];
	private readonly List<INativeLinkSession> _stopping = [];

	public AccessoryCoordinator(IUsbHost usb,
		Func<IUsbBulkPipe, string, INativeLinkSession> startSession,
		AccessoryReconnectPolicy policy,
		IReadOnlyList<string> identification,
		ILogger logger)
	{
		_identification = identification;
		_usb = usb;
		_startSession = startSession;
		_policy = policy;
		_logger = logger.ForContext<AccessoryCoordinator>();
	}

	public bool Available => _usb.Available;

	private volatile HashSet<string> _nativeSerials = new(StringComparer.Ordinal);

	public IReadOnlyList<NativeUsbDevice> Devices { get; private set; } = [];

	public bool IsNative(string serial) => _nativeSerials.Contains(serial);

	public IReadOnlyList<RememberedUsbDevice> Poll(AccessoryPollContext context, DateTimeOffset now)
	{
		lock (_gate)
		{
			var linked = new List<RememberedUsbDevice>();
			if (!_usb.Available)
			{
				Devices = [];
				return linked;
			}

			var seen = new HashSet<UsbPlugKey>();
			using (var list = _usb.Enumerate())
			{
				foreach (var device in list.Devices)
				{
					seen.Add(device.Plug);
					if (!_plugs.TryGetValue(device.Plug, out var plug))
					{
						plug = new Plug(device.Plug);
						_plugs[device.Plug] = plug;
					}

					var reappeared = plug.AbsentPolls > 0;
					plug.AbsentPolls = 0;
					if (AccessoryProtocol.IsAccessory(device.VendorId, device.ProductId))
					{
						PollAccessory(list, plug, device, context, now, reappeared, linked);
					}
					else
					{
						PollNormal(list, plug, device, context, now, reappeared, linked);
					}
				}
			}

			foreach (var plug in _plugs.Values.Where(plug => !seen.Contains(plug.Key)).ToList())
			{
				if (++plug.AbsentPolls >= UnplugPolls)
				{
					Retire(plug.Session);
					_plugs.Remove(plug.Key);
				}
			}

			_nativeSerials = _plugs.Values
				.Where(plug => plug.Serial is not null && (plug.InAccessoryMode || plug.AwaitingAccessory))
				.Select(plug => plug.Serial!)
				.ToHashSet(StringComparer.Ordinal);
			Devices = _plugs.Values.Where(plug => plug.Listed)
				.OrderBy(plug => plug.Key.ToString(), StringComparer.Ordinal)
				.Select(plug => Describe(plug, context))
				.ToList();
			return linked;
		}
	}

	public NativeUsbPickResult Pick(string deviceId)
	{
		lock (_gate)
		{
			var plug = _plugs.Values.FirstOrDefault(plug => plug.Listed && Id(plug) == deviceId);
			if (plug is null)
			{
				return NativeUsbPickResult.UnknownDevice;
			}

			if (plug.Blocked == SwitchBlock.NotSupported || plug.ByeAtLoss)
			{
				return NativeUsbPickResult.NotAllowed;
			}

			if (plug.AwaitingAccessory)
			{
				return NativeUsbPickResult.Picked;
			}

			if (!plug.InAccessoryMode)
			{
				plug.Switches = 0;
				plug.LinkedSinceSwitch = false;
				plug.LostAt = null;
				plug.Blocked = SwitchBlock.None;
			}

			plug.Picked = true;
			return NativeUsbPickResult.Picked;
		}
	}

	public void StopAll()
	{
		lock (_gate)
		{
			foreach (var plug in _plugs.Values)
			{
				Retire(plug.Session);
			}

			_plugs.Clear();
			_nativeSerials = new HashSet<string>(StringComparer.Ordinal);
			Devices = [];
		}
	}

	public Task WhenStopped()
	{
		lock (_gate)
		{
			_stopping.RemoveAll(session => session.Ended);
			return Task.WhenAll(_plugs.Values.Select(plug => plug.Session?.Completion ?? Task.CompletedTask)
				.Concat(_stopping.Select(session => session.Completion)));
		}
	}

	private void PollNormal(IUsbDeviceList list,
		Plug plug,
		UsbDeviceInfo device,
		AccessoryPollContext context,
		DateTimeOffset now,
		bool reappeared,
		List<RememberedUsbDevice> linked)
	{
		plug.AwaitingAccessory = false;
		var returnedFromAccessory = plug.InAccessoryMode;
		if (returnedFromAccessory)
		{
			plug.InAccessoryMode = false;
			ReleaseSession(plug, context, linked);
			plug.LostAt = now;
		}

		// Another phone can take the same port between two polls, so a device that went away is
		// identified again by its serial before anything it was allowed carries over to it.
		if (returnedFromAccessory || reappeared || plug.Identity != (device.VendorId, device.ProductId))
		{
			var knownSerial = plug.Serial;
			var hadHistory = plug.Identity is not null || knownSerial is not null;
			Inspect(list, plug, device);
			if (hadHistory && (plug.Serial is null || plug.Serial != knownSerial))
			{
				ResetHistory(plug);
			}
		}

		if (plug.Verdict is null)
		{
			plug.Listed = false;
			return;
		}

		plug.KnownToAdb = plug.Serial is { } serial && context.AdbSerials.Contains(serial);
		plug.Listed = true;
		if (!context.BridgeAvailable)
		{
			return;
		}

		if (MaySwitch(plug, context, now))
		{
			Switch(list, plug, device);
		}
	}

	private void PollAccessory(IUsbDeviceList list,
		Plug plug,
		UsbDeviceInfo device,
		AccessoryPollContext context,
		DateTimeOffset now,
		bool reappeared,
		List<RememberedUsbDevice> linked)
	{
		plug.InAccessoryMode = true;
		plug.Listed = true;
		if (plug.Session is { Ended: true, EverLinked: false })
		{
			plug.ReopenNotBefore = now + ReopenBackoff;
		}

		if (plug.Session is { Ended: true })
		{
			ReleaseSession(plug, context, linked);
		}

		if (plug.Session is { } session)
		{
			if (session.IsLinked)
			{
				MarkLinked(plug, context, linked);
			}

			return;
		}

		if (!context.BridgeAvailable || plug.ByeAtLoss || now < plug.ReopenNotBefore)
		{
			return;
		}

		if (!plug.AccessoryInspected || reappeared)
		{
			var switchedByUs = plug.AwaitingAccessory;
			plug.AccessoryInspected = true;
			plug.AwaitingAccessory = false;
			var strings = list.ReadStrings(device, []);
			if (!switchedByUs && plug.Serial is { } knownSerial && strings?.Serial != knownSerial)
			{
				ResetHistory(plug);
				plug.Serial = null;
				plug.Strings = null;
			}

			plug.Strings ??= strings;
			plug.Serial ??= strings?.Serial;
		}

		var allowed = plug.Switches > 0 || plug.Picked || IsRemembered(plug, context);
		if (!allowed || list.OpenAccessory(device) is not { } pipe)
		{
			return;
		}

		plug.Session = _startSession(pipe, NativeUsbDeviceKeys.For(plug.Serial, plug.Key.ToString()));
		_logger.Write(plug.Opens++ == 0 ? LogEventLevel.Information : LogEventLevel.Debug,
			"Opened the accessory link on USB {Plug}",
			plug.Key.ToString());
	}

	private static void Inspect(IUsbDeviceList list, Plug plug, UsbDeviceInfo device)
	{
		plug.Identity = (device.VendorId, device.ProductId);
		plug.Verdict = null;
		plug.Strings = null;
		plug.Serial = null;
		if (list.ReadInterfaces(device) is not { } interfaces ||
			AndroidCandidateFilter.ClassifyDescriptors(device, interfaces) is not { } verdict ||
			list.ReadStrings(device, verdict.MtpNameIndexes) is not { } strings ||
			!AndroidCandidateFilter.AcceptStrings(verdict, strings))
		{
			return;
		}

		plug.Verdict = verdict;
		plug.Strings = strings;
		plug.Serial = strings.Serial;
	}

	private bool MaySwitch(Plug plug, AccessoryPollContext context, DateTimeOffset now)
	{
		if (!(plug.Picked || IsRemembered(plug, context)) || plug.Blocked != SwitchBlock.None)
		{
			return false;
		}

		if (plug.Switches == 0)
		{
			return true;
		}

		if (!plug.LinkedSinceSwitch ||
			(plug.ByeAtLoss && !_policy.ReSwitchAfterBye) ||
			plug.Switches - 1 >= _policy.ReSwitchDelays.Count)
		{
			return false;
		}

		return plug.LostAt is { } lostAt && now - lostAt >= _policy.ReSwitchDelays[plug.Switches - 1];
	}

	private void Switch(IUsbDeviceList list, Plug plug, UsbDeviceInfo device)
	{
		var outcome = list.SwitchToAccessory(device, _identification);
		_logger.Information("Switching USB {Plug} to accessory mode: {Outcome}", plug.Key.ToString(), outcome);
		switch (outcome)
		{
			case AccessorySwitchOutcome.Switched:
				plug.Switches++;
				plug.AwaitingAccessory = true;
				plug.LinkedSinceSwitch = false;
				plug.ByeAtLoss = false;
				plug.LostAt = null;
				break;
			case AccessorySwitchOutcome.NotSupported:
				plug.Blocked = SwitchBlock.NotSupported;
				break;
			default:
				plug.Blocked = SwitchBlock.Failed;
				break;
		}
	}

	private void ReleaseSession(Plug plug, AccessoryPollContext context, List<RememberedUsbDevice> linked)
	{
		if (plug.Session is not { } session)
		{
			return;
		}

		Retire(session);
		plug.Session = null;
		if (session.EverLinked)
		{
			MarkLinked(plug, context, linked);
		}

		if (session.EverLinked || session.ByeReceived)
		{
			plug.ByeAtLoss = session.ByeReceived;
		}
	}

	private void ResetHistory(Plug plug)
	{
		Retire(plug.Session);
		plug.Session = null;
		plug.Picked = false;
		plug.Switches = 0;
		plug.LinkedSinceSwitch = false;
		plug.ByeAtLoss = false;
		plug.LostAt = null;
		plug.Blocked = SwitchBlock.None;
		plug.AwaitingAccessory = false;
		plug.ReopenNotBefore = DateTimeOffset.MinValue;
	}

	private void Retire(INativeLinkSession? session)
	{
		if (session is null)
		{
			return;
		}

		session.Stop();
		_stopping.RemoveAll(stopped => stopped.Ended);
		_stopping.Add(session);
	}

	private static void MarkLinked(Plug plug, AccessoryPollContext context, List<RememberedUsbDevice> linked)
	{
		if (plug.LinkedSinceSwitch)
		{
			return;
		}

		plug.LinkedSinceSwitch = true;
		if (plug.Serial is { } serial && !context.RememberedSerials.Contains(serial))
		{
			linked.Add(new RememberedUsbDevice(serial, DisplayName(plug.Strings)));
		}
	}

	private static bool IsRemembered(Plug plug, AccessoryPollContext context)
		=> plug.Serial is { } serial && context.RememberedSerials.Contains(serial);

	private NativeUsbDevice Describe(Plug plug, AccessoryPollContext context)
		=> new(Id(plug),
			NativeUsbPlatform.Android,
			plug.Serial,
			plug.Strings?.Manufacturer,
			plug.Strings?.Product,
			StateOf(plug, context),
			plug.Picked,
			IsRemembered(plug, context),
			plug.KnownToAdb);

	private NativeUsbDeviceState StateOf(Plug plug, AccessoryPollContext context)
	{
		if (plug.InAccessoryMode)
		{
			return plug.Session switch
			{
				{ IsLinked: true } => NativeUsbDeviceState.Linked,
				{ ByeReceived: true } => NativeUsbDeviceState.Closed,
				null when plug.ByeAtLoss => NativeUsbDeviceState.Closed,
				not null => NativeUsbDeviceState.WaitingForApp,
				_ => plug.Switches > 0 || plug.Picked || IsRemembered(plug, context)
					? NativeUsbDeviceState.Waiting
					: NativeUsbDeviceState.Available
			};
		}

		if (plug.Blocked == SwitchBlock.NotSupported)
		{
			return NativeUsbDeviceState.NotSupported;
		}

		if (plug.AwaitingAccessory)
		{
			return NativeUsbDeviceState.Switching;
		}

		if (plug.KnownToAdb && !plug.Picked && !IsRemembered(plug, context))
		{
			return NativeUsbDeviceState.ServedByAdb;
		}

		if (plug.Blocked == SwitchBlock.Failed || (plug.Switches > 0 && !plug.LinkedSinceSwitch))
		{
			return NativeUsbDeviceState.Stopped;
		}

		if (plug.Switches > 0 && plug.ByeAtLoss && !_policy.ReSwitchAfterBye)
		{
			return NativeUsbDeviceState.Closed;
		}

		if (plug.Switches - 1 >= _policy.ReSwitchDelays.Count)
		{
			return NativeUsbDeviceState.Stopped;
		}

		return plug.Picked || IsRemembered(plug, context)
			? NativeUsbDeviceState.Waiting
			: NativeUsbDeviceState.Available;
	}

	private static string Id(Plug plug) => "android-" + plug.Key;

	private static string? DisplayName(UsbDeviceStrings? strings)
	{
		var product = strings?.Product?.Trim();
		var manufacturer = strings?.Manufacturer?.Trim();
		if (string.IsNullOrEmpty(product))
		{
			return string.IsNullOrEmpty(manufacturer) ? null : manufacturer;
		}

		return string.IsNullOrEmpty(manufacturer) || product.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
			? product
			: $"{manufacturer} {product}";
	}

	private enum SwitchBlock
	{
		None,

		NotSupported,

		Failed
	}

	private sealed class Plug
	{
		public Plug(UsbPlugKey key)
		{
			Key = key;
		}

		public UsbPlugKey Key { get; }

		public (ushort VendorId, ushort ProductId)? Identity { get; set; }

		public AndroidDescriptorVerdict? Verdict { get; set; }

		public UsbDeviceStrings? Strings { get; set; }

		public string? Serial { get; set; }

		public bool Listed { get; set; }

		public int AbsentPolls { get; set; }

		public bool KnownToAdb { get; set; }

		public bool Picked { get; set; }

		public int Switches { get; set; }

		public bool LinkedSinceSwitch { get; set; }

		public bool ByeAtLoss { get; set; }

		public DateTimeOffset? LostAt { get; set; }

		public SwitchBlock Blocked { get; set; }

		public bool InAccessoryMode { get; set; }

		public bool AccessoryInspected { get; set; }

		public bool AwaitingAccessory { get; set; }

		public DateTimeOffset ReopenNotBefore { get; set; } = DateTimeOffset.MinValue;

		public int Opens { get; set; }

		public INativeLinkSession? Session { get; set; }
	}
}
