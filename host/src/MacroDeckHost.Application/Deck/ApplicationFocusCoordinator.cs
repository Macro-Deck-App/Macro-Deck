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

	private readonly Lock _sync = new();

	private readonly Dictionary<Guid, Guid> _currentFolderByDevice = [];

	private readonly Dictionary<Guid, ChainState> _chainByDevice = [];

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

				if (chain.ReturnOnFocusLoss &&
					chain.BaselineFolderId != Guid.Empty &&
					_folderCache.GetFolderById(chain.BaselineFolderId) is not null)
				{
					toNavigate.Add((deviceId, chain.BaselineFolderId, Guid.NewGuid()));
				}
			}

			foreach (var (deviceId, match) in matched)
			{
				if (!IsOnline(deviceId))
				{
					continue;
				}

				if (_chainByDevice.TryGetValue(deviceId, out var existingChain) &&
					string.Equals(existingChain.OwningIdentity,
						match.Rule.ApplicationIdentity,
						StringComparison.Ordinal) &&
					existingChain.OwnedFolderId == match.Folder.Id)
				{
					// Idempotent: the same rule already owns this device's current folder - a repeated
					// OnFocusChanged for the still-focused app must not mint a new token or re-navigate.
					continue;
				}

				var baseline = _chainByDevice.TryGetValue(deviceId, out var priorChain)
					? priorChain.BaselineFolderId
					: _currentFolderByDevice.GetValueOrDefault(deviceId, Guid.Empty);

				var token = Guid.NewGuid();
				_chainByDevice[deviceId] = new ChainState(baseline,
					match.Folder.Id,
					token,
					match.Rule.ApplicationIdentity,
					match.Rule.ReturnOnFocusLoss);
				toNavigate.Add((deviceId, match.Folder.Id, token));
			}
		}

		foreach (var (deviceId, folderId, token) in toNavigate)
		{
			await _navigator.ChangeFolderOnDeviceAsync(deviceId, folderId, token, cancellationToken);
		}
	}

	public async Task OnFolderReported(Guid deviceId,
		Guid folderId,
		string? navigationToken,
		bool isResync,
		CancellationToken cancellationToken)
	{
		(Guid DeviceId, Guid FolderId, Guid Token)? toNavigate = null;

		lock (_sync)
		{
			_currentFolderByDevice[deviceId] = folderId;

			if (_chainByDevice.TryGetValue(deviceId, out var chain))
			{
				var tokenMatches = navigationToken is not null &&
					Guid.TryParse(navigationToken, out var parsedToken) &&
					parsedToken == chain.Token;

				if (!tokenMatches && !isResync)
				{
					_chainByDevice.Remove(deviceId);
				}
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
				var stillValid = folder is not null &&
					folder.FocusRules.Any(rule =>
						rule.Enabled &&
						rule.DeviceId == deviceId &&
						string.Equals(rule.ApplicationIdentity, chain.OwningIdentity, StringComparison.Ordinal));

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

	private (Guid DeviceId, Guid FolderId, Guid Token)? TryAcquireForDevice(Guid deviceId,
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

		if (_chainByDevice.TryGetValue(deviceId, out var existingChain) &&
			string.Equals(existingChain.OwningIdentity, match.Rule.ApplicationIdentity, StringComparison.Ordinal) &&
			existingChain.OwnedFolderId == match.Folder.Id &&
			(reportedFolderId is not { } reported || reported == match.Folder.Id))
		{
			// Idempotent: the same rule already owns this device's current folder - and, when a
			// resync says what the device actually shows, it genuinely still shows it. A repeated
			// acquire for the still-focused app must not mint a new token or re-navigate.
			return null;
		}

		var baseline = _chainByDevice.TryGetValue(deviceId, out var priorChain)
			? priorChain.BaselineFolderId
			: _currentFolderByDevice.GetValueOrDefault(deviceId, Guid.Empty);

		var token = Guid.NewGuid();
		_chainByDevice[deviceId] = new ChainState(baseline,
			match.Folder.Id,
			token,
			match.Rule.ApplicationIdentity,
			match.Rule.ReturnOnFocusLoss);
		return (deviceId, match.Folder.Id, token);
	}

	// A provider device holds no WebSocket connection, so connection counts alone would report every one
	// of them as offline and no focus rule could ever acquire one.
	private bool IsOnline(Guid deviceId)
		=> (_deviceConnections.OnlineDeviceConnectionCounts().TryGetValue(deviceId, out var count) && count > 0) ||
			_providerPresence.IsOnline(deviceId);
}
