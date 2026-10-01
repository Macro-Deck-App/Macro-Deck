using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Plugin.Testing.Internal;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>ui</c> capability, seen from the host: lists the plugin's <c>[UiPreview]</c> scenarios and opens
/// them as developer-preview sessions, the way Developer Tools does.
/// </summary>
public sealed class UiTestClient
{
	private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

	private readonly ICapabilityInvoker _invoker;
	private readonly PluginConnection _connection;

	internal UiTestClient(ICapabilityInvoker invoker, PluginConnection connection)
	{
		_invoker = invoker;
		_connection = connection;
	}

	/// <summary>What the plugin declares about its UI surfaces and previews.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Ui,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Ui.Describe,
			null,
			options);

	/// <summary>The previews the plugin declares. Empty for a plugin that declares none or predates previews.</summary>
	/// <exception cref="InvalidOperationException">The plugin failed the describe call.</exception>
	public async Task<IReadOnlyList<UiPreviewDescriptorDto>> GetPreviewsAsync(CapabilityInvokeOptions? options = null)
	{
		var outcome = await DescribeAsync(options).ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			throw new InvalidOperationException(
				$"The plugin failed the ui describe call: {outcome.Error?.Message ?? "no reply"}.");
		}

		return outcome.DataAs<UiDescribePayload>()?.Previews ?? [];
	}

	/// <summary>
	/// Opens the developer preview <paramref name="previewId" /> and waits for the first full tree. The
	/// session stays open until <see cref="CloseAsync" />, so a caller reads <see cref="FindResource" />
	/// before closing.
	/// </summary>
	/// <param name="previewId">An id from <see cref="GetPreviewsAsync" />.</param>
	/// <param name="profile">The advisory <c>config</c> or <c>widget</c> profile the preview declared.</param>
	/// <param name="surfaceAttributes">Further surface attributes the scenario can read, such as <c>cornerRadius</c>.</param>
	/// <param name="timeout">How long to wait for the tree. Defaults to 30 seconds.</param>
	/// <exception cref="PluginTestTimeoutException">The plugin accepted the open but served no tree in time.</exception>
	public async Task<UiPreviewOutcome> OpenPreviewAsync(
		string previewId,
		string? profile = null,
		IReadOnlyDictionary<string, JsonElement>? surfaceAttributes = null,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(previewId);

		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		foreach (var (key, value) in surfaceAttributes ?? new Dictionary<string, JsonElement>())
		{
			attributes[key] = value;
		}

		attributes[UiDeveloperPreviewSurfaceAttributes.PreviewId] = JsonSerializer.SerializeToElement(previewId);

		if (profile is not null)
		{
			attributes[UiDeveloperPreviewSurfaceAttributes.Profile] = JsonSerializer.SerializeToElement(profile);
		}

		var sessionId = Guid.CreateVersion7().ToString();
		var frame = _connection.UiPreviews.Expect(sessionId);

		var opened = await _invoker.InvokeAsync(CapabilityKinds.Ui,
				ProviderCapabilityId.LocalId,
				CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = sessionId,
					SurfaceKind = UiSurfaceKinds.DeveloperPreview,
					SessionMode = "exclusive",
					SurfaceAttributes = JsonSerializer.SerializeToElement(attributes),
					UiModelVersion = UiModelVersions.Current
				})
			.ConfigureAwait(false);

		var result = opened.DataAs<UiSessionOpenResult>();

		if (!opened.Succeeded || result is not { Accepted: true })
		{
			_connection.UiPreviews.Forget(sessionId);

			return new UiPreviewOutcome
			{
				PreviewId = previewId,
				Accepted = false,
				FailureReason = result?.RejectionReason ?? opened.Error?.Message ?? "The plugin did not answer the open."
			};
		}

		await _invoker.InvokeAsync(CapabilityKinds.Ui,
				ProviderCapabilityId.LocalId,
				CapabilityOperations.Ui.SessionSnapshot,
				new UiSessionSnapshotArguments { SessionId = sessionId })
			.ConfigureAwait(false);

		using var deadline = new CancellationTokenSource(timeout ?? _defaultTimeout);

		try
		{
			var served = await frame.WaitAsync(deadline.Token).ConfigureAwait(false);

			return new UiPreviewOutcome
			{
				PreviewId = previewId,
				SessionId = sessionId,
				Accepted = served.Tree is not null,
				Tree = served.Tree,
				FailureReason = served.FaultCode is null ? null : $"{served.FaultCode}: {served.FaultMessage}"
			};
		}
		catch (OperationCanceledException)
		{
			_connection.UiPreviews.Forget(sessionId);
			throw new PluginTestTimeoutException($"The plugin served no tree for the preview '{previewId}' in time.");
		}
	}

	/// <summary>Closes a session <see cref="OpenPreviewAsync" /> opened. Closing an unknown session is not an error.</summary>
	public async Task CloseAsync(string sessionId)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);

		_connection.UiPreviews.Forget(sessionId);

		await _invoker.InvokeAsync(CapabilityKinds.Ui,
				ProviderCapabilityId.LocalId,
				CapabilityOperations.Ui.SessionClose,
				new UiSessionCloseArguments { SessionId = sessionId })
			.ConfigureAwait(false);
	}

	/// <summary>The bytes of a resource the plugin registered, by the <c>resourceId</c> a tree references; <c>null</c> when none matches.</summary>
	public FakeUiResource? FindResource(string resourceId)
	{
		ArgumentException.ThrowIfNullOrEmpty(resourceId);

		return _connection.UiResources.Resources.Values
			.FirstOrDefault(resource => string.Equals(resource.Handle.ResourceId, resourceId, StringComparison.Ordinal));
	}
}
