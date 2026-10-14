using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Email;

namespace MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;

internal sealed class EmailInboxCapabilityHandler : ICapabilityHandler, IDisposable
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private static readonly TimeSpan _coalesceWindow = TimeSpan.FromMilliseconds(250);

	private readonly IPluginCatalogNotifier? _catalogNotifier;
	private readonly Lock _gate = new();
	private readonly PluginMetadata _metadata;
	private readonly Dictionary<string, ITimer> _pendingInboxes = new(StringComparer.Ordinal);
	private readonly IReadOnlyList<IEmailInboxProvider> _providers;
	private readonly TimeProvider _time;
	private bool _disposed;
	private ITimer? _pendingInboxList;

	public EmailInboxCapabilityHandler(
		IEnumerable<IPluginIntegration> integrations,
		PluginMetadata metadata,
		IPluginCatalogNotifier? catalogNotifier = null,
		TimeProvider? timeProvider = null)
	{
		_providers = [.. integrations.OfType<IEmailInboxProvider>()];
		_metadata = metadata;
		_catalogNotifier = catalogNotifier;
		_time = timeProvider ?? TimeProvider.System;

		foreach (var provider in _providers)
		{
			provider.Changed += OnProviderChanged;
		}
	}

	public string Kind => CapabilityKinds.EmailInbox;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
	{
		return _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.EmailInbox, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];
	}

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (string.Equals(invocation.Operation, CapabilityOperations.EmailInbox.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No email inbox provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.EmailInbox.Inboxes => Task.FromResult(
				CapabilityInvocationResult.Ok(new EmailInboxesResult { Inboxes = BuildInboxes() })),
			CapabilityOperations.EmailInbox.Snapshot => SnapshotAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The email inbox capability has no operation '{invocation.Operation}'."))
		};
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			foreach (var provider in _providers)
			{
				provider.Changed -= OnProviderChanged;
			}

			foreach (var timer in _pendingInboxes.Values)
			{
				timer.Dispose();
			}

			_pendingInboxes.Clear();
			_pendingInboxList?.Dispose();
			_pendingInboxList = null;
		}
	}

	private CapabilityInvocationResult Describe()
	{
		return CapabilityInvocationResult.Ok(new EmailInboxDescribePayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name))
				?? _metadata.Name,
			Inboxes = BuildInboxes()
		});
	}

	// An inbox id announced by two providers belongs to the first one, as FindProvider routes it.
	private IReadOnlyList<EmailInboxDto> BuildInboxes()
	{
		return
		[
			.. _providers.SelectMany(provider => provider.GetInboxes())
				.DistinctBy(inbox => inbox.Id, StringComparer.Ordinal)
				.Take(ProtocolLimits.MaxEmailInboxes)
				.Select(EmailInboxDescriptorMapper.ToDto)
		];
	}

	private async Task<CapabilityInvocationResult> SnapshotAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<EmailInboxSnapshotArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				$"The {invocation.Operation} operation requires arguments.");
		}

		var provider = FindProvider(arguments.InboxId);
		if (provider is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No email inbox '{arguments.InboxId}' is currently available.");
		}

		var maxMessages = Math.Clamp(arguments.MaxMessages, 1, ProtocolLimits.MaxEmailMessages);
		var snapshot = await provider.GetSnapshotAsync(arguments.InboxId,
				new EmailMessageQuery { MaxMessages = maxMessages, UnreadOnly = arguments.UnreadOnly },
				cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(EmailInboxDescriptorMapper.ToSnapshotResult(snapshot, maxMessages));
	}

	private IEmailInboxProvider? FindProvider(string inboxId)
	{
		return _providers.FirstOrDefault(provider => provider.GetInboxes()
			.Any(inbox => string.Equals(inbox.Id, inboxId, StringComparison.Ordinal)));
	}

	private void OnProviderChanged(object? sender, EmailInboxChangedEventArgs e)
	{
		var inboxId = e?.InboxId;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (inboxId is null)
			{
				_pendingInboxList ??= _time.CreateTimer(_ => Flush(null),
					null,
					_coalesceWindow,
					Timeout.InfiniteTimeSpan);
				return;
			}

			if (!_pendingInboxes.ContainsKey(inboxId))
			{
				_pendingInboxes[inboxId] = _time.CreateTimer(_ => Flush(inboxId),
					null,
					_coalesceWindow,
					Timeout.InfiniteTimeSpan);
			}
		}
	}

	private void Flush(string? inboxId)
	{
		ITimer? timer;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (inboxId is null)
			{
				timer = _pendingInboxList;
				_pendingInboxList = null;
			}
			else
			{
				_pendingInboxes.Remove(inboxId, out timer);
			}
		}

		timer?.Dispose();
		_catalogNotifier?.CatalogChanged(CapabilityKinds.EmailInbox,
			inboxId,
			inboxId is null ? "Inbox list changed" : "Inbox changed");
	}
}
