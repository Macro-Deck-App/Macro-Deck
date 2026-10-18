using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Ui;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

internal sealed class RemoteUiEventOrigin(
	IHostInvoker invoker,
	ModalResultStore? modalResults,
	ILogger logger,
	string sessionId,
	string? clientId,
	string eventName) : IUiEventOrigin
{
	private static readonly JsonSerializerOptions _valueOptions = new(JsonSerializerDefaults.Web);

	public string? ClientId { get; } = string.IsNullOrEmpty(clientId) ? null : clientId;

	public async Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
	{
		if (ClientId is null)
		{
			return false;
		}

		var result = await InvokeAsync(HostOperations.Ui.OriginReportFailure,
				new UiOriginReportFailureArguments
				{
					SessionId = sessionId, ClientId = ClientId, EventName = eventName, Message = message
				},
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Deserialize<UiOriginReportFailureResult>(PluginProtocolJson.Options)?.Delivered == true;
	}

	public async Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		return await OpenAsync(modal, false, cancellationToken).ConfigureAwait(false) is not null;
	}

	public async Task<ModalResult<T>> ShowModalAsync<T>(
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
	{
		if (modalResults is null)
		{
			return ModalResult.FromCancellation<T>();
		}

		var modalId = await OpenAsync(modal, true, cancellationToken).ConfigureAwait(false);
		if (modalId is null)
		{
			return ModalResult.FromCancellation<T>();
		}

		var answer = await modalResults.Await(modalId, cancellationToken).ConfigureAwait(false);
		if (answer.Cancelled || answer.Value is not { } value)
		{
			return ModalResult.FromCancellation<T>();
		}

		try
		{
			return ModalResult.FromValue(value.Deserialize<T>(_valueOptions));
		}
		catch (JsonException exception)
		{
			logger.HostCallbackFailed(HostApis.Ui, HostOperations.Ui.OriginShowModal, exception);
			return ModalResult.FromCancellation<T>();
		}
	}

	public async Task<bool> OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(url);

		if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
		{
			throw new ArgumentException("Only absolute http and https URLs can be opened.", nameof(url));
		}

		if (ClientId is null)
		{
			return false;
		}

		var result = await InvokeAsync(HostOperations.Ui.OriginOpenUrl,
				new UiOriginOpenUrlArguments { SessionId = sessionId, ClientId = ClientId, Url = url.AbsoluteUri },
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Deserialize<UiOriginOpenUrlResult>(PluginProtocolJson.Options)?.Delivered == true;
	}

	private async Task<string?> OpenAsync(ModalDefinition modal, bool awaitResult, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(modal);

		if (ClientId is null)
		{
			return null;
		}

		var result = await InvokeAsync(HostOperations.Ui.OriginShowModal,
				new UiOriginShowModalArguments
				{
					SessionId = sessionId,
					ClientId = ClientId,
					ViewId = modal.ViewId,
					Title = modal.Title,
					Data = modal.Data,
					AwaitResult = awaitResult
				},
				cancellationToken)
			.ConfigureAwait(false);

		var opened = result?.Deserialize<UiOriginShowModalResult>(PluginProtocolJson.Options);
		return opened is { Opened: true, ModalId.Length: > 0 } ? opened.ModalId : null;
	}

	private async Task<JsonElement?> InvokeAsync(
		string operation,
		object arguments,
		CancellationToken cancellationToken)
	{
		try
		{
			return await invoker.InvokeAsync(HostApis.Ui, operation, arguments, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
			when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			// A Macro Deck that predates event origins: the documented answer is false or a cancellation.
			return null;
		}
		catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
		{
			logger.HostCallbackFailed(HostApis.Ui, operation, exception);
			return null;
		}
	}
}
