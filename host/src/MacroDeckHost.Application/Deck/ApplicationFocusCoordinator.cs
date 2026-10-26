using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Domain.Entities;
using Serilog;

namespace MacroDeckHost.Application.Deck;

public sealed class ApplicationFocusCoordinator : IApplicationFocusCoordinator
{
	private sealed record ChainState(
		Guid BaselineFolderId,
		Guid OwnedFolderId,
		Guid Token,
		string OwningIdentity,
		bool ReturnOnFocusLoss);

	private const int IssuedNavigationCapacity = 16;

	private readonly Lock _sync = new();

	private readonly Dictionary<Guid, Guid> _currentFolderByDevice = [];

	private readonly Dictionary<Guid, ChainState> _chainByDevice = [];

	private readonly Dictionary<Guid, List<(Guid Token, Guid FolderId)>> _issuedByDevice = [];

	private FocusedApplication? _current;

	private readonly IFolderCache _folderCache;
	private readonly DeviceConnectionTracker _deviceConnections;
	private readonly ProviderDevicePresenceTracker _providerPresence;
	private readonly IDeviceDeckNavigator _navigator;
	private readonly ILogger _logger;

	public ApplicationFocusCoordinator(
		IFolderCache folderCache,
		DeviceConnectionTracker deviceConnections,
		IDeviceDeckNavigator navigator,
		ILogger logger,
		ProviderDevicePresenceTracker providerPresence)
	{
		_folderCache = folderCache;
		_deviceConnections = deviceConnections;
		_providerPresence = providerPresence;
		_navigator = navigator;
		_logger = logger.ForContext<ApplicationFocusCoordinator>();
	}

	public async Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
	{
		List<(Guid DeviceId, Guid FolderId, Guid Token)> toNavigate;

		lock (_sync)
		{
			_current = app;
			var matched = ComputeMatches(app);
			toNavigate = [];

			foreach (var deviceId in _chainByDevice.Keys.ToList())
			{
				if (matched.ContainsKey(deviceId))
				{
					continue;
				}

				var chain = _chainByDevice[deviceId];
				_chainByDevice.Remove(deviceId);

				if (chain.ReturnOnFocusLoss
					&& chain.BaselineFolderId != Guid.Empty
					&& _folderCache.GetFolderById(chain.BaselineFolderId) is not null)
				{
					toNavigate.Add(Issue(deviceId, chain.BaselineFolderId));
				}
			}

			foreach (var (deviceId, match) in matched)
			{
				if (!IsOnline(deviceId))
				{
					continue;
				}

				if (_chainByDevice.TryGetValue(deviceId, out var existingChain)
					&& string.Equals(existingChain.OwningIdentity,
						match.Rule.ApplicationIdentity,
						StringComparison.Ordinal)
					&& existingChain.OwnedFolderId == match.Folder.Id)
				{
					// Idempotent: the same rule already owns this device's current folder - a repeated
					// OnFocusChanged for the still-focused app must not mint a new token or re-navigate.
					continue;
				}

				toNavigate.Add(Acquire(deviceId, match.Folder, match.Rule));
			}
		}

		foreach (var (deviceId, folderId, token) in toNavigate)
		{
			await _navigator.ChangeFolderOnDeviceAsync(deviceId, folderId, token, cancellationToken);
		}
	}

	public async Task OnFolderReported(
		Guid deviceId,
		Guid folderId,
		string? navigationToken,
		bool isResync,
		CancellationToken cancellationToken)
	{
		(Guid DeviceId, Guid FolderId, Guid Token)? toNavigate = null;

		lock (_sync)
		{
			_currentFolderByDevice[deviceId] = folderId;

			if (isResync)
			{
				_issuedByDevice.Remove(deviceId);
			}
			else if (!IsOwnNavigation(deviceId, navigationToken))
			{
				_chainByDevice.Remove(deviceId);
				_issuedByDevice.Remove(deviceId);
			}

			if (isResync && _current is { } app)
			{
				toNavigate = TryAcquireForDevice(deviceId, app, folderId);
			}
		}

		if (toNavigate is { } nav)
		{
			await _navigator.ChangeFolderOnDeviceAsync(nav.DeviceId, nav.FolderId, nav.Token, cancellationToken);
		}
	}

	public async Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
	{
		(Guid DeviceId, Guid FolderId, Guid Token)? toNavigate = null;

		lock (_sync)
		{
			if (!online)
			{
				_chainByDevice.Remove(deviceId);
				_issuedByDevice.Remove(deviceId);
			}
			else if (_current is { } app)
			{
				toNavigate = TryAcquireForDevice(deviceId, app);
			}
		}

		if (toNavigate is { } nav)
		{
			await _navigator.ChangeFolderOnDeviceAsync(nav.DeviceId, nav.FolderId, nav.Token, cancellationToken);
		}
	}

	public Task OnRulesChanged(CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			foreach (var deviceId in _chainByDevice.Keys.ToList())
			{
				var chain = _chainByDevice[deviceId];
				var folder = _folderCache.GetFolderById(chain.OwnedFolderId);
				var stillValid = folder is not null
					&& folder.FocusRules.Any(rule =>
						rule.Enabled
						&& rule.DeviceId == deviceId
						&& string.Equals(rule.ApplicationIdentity, chain.OwningIdentity, StringComparison.Ordinal));

				if (!stillValid)
				{
					_chainByDevice.Remove(deviceId);
				}
			}
		}

		return Task.CompletedTask;
	}

	private Dictionary<Guid, (FolderEntity Folder, FolderFocusRule Rule)> ComputeMatches(FocusedApplication app)
	{
		var candidatesByDevice = new Dictionary<Guid, List<(FolderEntity Folder, FolderFocusRule Rule)>>();
		foreach (var folder in _folderCache.GetAllFolders())
		{
			foreach (var rule in folder.FocusRules)
			{
				if (!ApplicationIdentityMatcher.Matches(rule, app))
				{
					continue;
				}

				if (!candidatesByDevice.TryGetValue(rule.DeviceId, out var candidates))
				{
					candidates = [];
					candidatesByDevice[rule.DeviceId] = candidates;
				}

				candidates.Add((folder, rule));
			}
		}

		var result = new Dictionary<Guid, (FolderEntity Folder, FolderFocusRule Rule)>();
		foreach (var (deviceId, candidates) in candidatesByDevice)
		{
			if (candidates.Count == 1)
			{
				result[deviceId] = candidates[0];
				continue;
			}

			var winner = candidates.OrderBy(c => c.Folder.Id).ThenBy(c => c.Rule.Id).First();
			_logger.Warning(
				"Multiple enabled focus rules target device {DeviceId} for the same focused application; using folder {FolderId} rule {RuleId}",
				deviceId,
				winner.Folder.Id,
				winner.Rule.Id);
			result[deviceId] = winner;
		}

		return result;
	}

	private (Guid DeviceId, Guid FolderId, Guid Token)? TryAcquireForDevice(
		Guid deviceId,
		FocusedApplication app,
		Guid? reportedFolderId = null)
	{
		if (!IsOnline(deviceId))
		{
			return null;
		}

		if (!ComputeMatches(app).TryGetValue(deviceId, out var match))
		{
			return null;
		}

		if (_chainByDevice.TryGetValue(deviceId, out var existingChain)
			&& string.Equals(existingChain.OwningIdentity, match.Rule.ApplicationIdentity, StringComparison.Ordinal)
			&& existingChain.OwnedFolderId == match.Folder.Id
			&& (reportedFolderId is not { } reported || reported == match.Folder.Id))
		{
			// Idempotent: the same rule already owns this device's current folder - and, when a
			// resync says what the device actually shows, it genuinely still shows it. A repeated
			// acquire for the still-focused app must not mint a new token or re-navigate.
			return null;
		}

		return Acquire(deviceId, match.Folder, match.Rule);
	}

	private (Guid DeviceId, Guid FolderId, Guid Token) Acquire(Guid deviceId, FolderEntity folder, FolderFocusRule rule)
	{
		var baseline = BaselineFor(deviceId);
		var navigation = Issue(deviceId, folder.Id);
		_chainByDevice[deviceId] = new ChainState(baseline,
			folder.Id,
			navigation.Token,
			rule.ApplicationIdentity,
			rule.ReturnOnFocusLoss);
		return navigation;
	}

	private Guid BaselineFor(Guid deviceId)
	{
		if (_chainByDevice.TryGetValue(deviceId, out var chain))
		{
			return chain.BaselineFolderId;
		}

		return _issuedByDevice.TryGetValue(deviceId, out var issued) && issued.Count > 0
			? issued[^1].FolderId
			: _currentFolderByDevice.GetValueOrDefault(deviceId, Guid.Empty);
	}

	// Recorded under the lock before the navigator runs: a device surface reports the folder
	// synchronously from inside ChangeFolderOnDeviceAsync.
	private (Guid DeviceId, Guid FolderId, Guid Token) Issue(Guid deviceId, Guid folderId)
	{
		if (!_issuedByDevice.TryGetValue(deviceId, out var issued))
		{
			issued = [];
			_issuedByDevice[deviceId] = issued;
		}

		var token = Guid.NewGuid();
		issued.Add((token, folderId));
		if (issued.Count > IssuedNavigationCapacity)
		{
			issued.RemoveAt(0);
		}

		return (deviceId, folderId, token);
	}

	// Entries stay after an acknowledgement: every connection of a device echoes the same token.
	private bool IsOwnNavigation(Guid deviceId, string? navigationToken)
	{
		if (navigationToken is null || !Guid.TryParse(navigationToken, out var token))
		{
			return false;
		}

		return _chainByDevice.TryGetValue(deviceId, out var chain) && chain.Token == token
			|| _issuedByDevice.TryGetValue(deviceId, out var issued)
			&& issued.Exists(entry => entry.Token == token);
	}

	// A provider device holds no WebSocket connection, so connection counts alone would report every one
	// of them as offline and no focus rule could ever acquire one.
	private bool IsOnline(Guid deviceId)
	{
		return _deviceConnections.OnlineDeviceConnectionCounts().TryGetValue(deviceId, out var count) && count > 0
			|| _providerPresence.IsOnline(deviceId);
	}
}
