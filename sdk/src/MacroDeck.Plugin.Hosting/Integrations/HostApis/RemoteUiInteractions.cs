using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Ui;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IUiInteractions" /> over <c>host.invoke</c> against
/// <see cref="HostApis.ActionInteractions" />, one per <c>actions/execute</c> invocation - see
/// <c>ActionsCapabilityHandler.ExecuteAsync</c>, which builds one from the ambient correlation id so the
/// host can verify a modal belongs to a live execution of this plugin before opening it.
/// </summary>
/// <remarks>
/// Opening and answering are two exchanges, not one. <c>host.invoke</c> carries a fixed request deadline
/// and a person is not bound by it, so the open call returns a modal id at once and the answer arrives
/// later as a <c>ui</c>/<c>modal.result</c> invoke that <see cref="ModalResultStore" /> matches back to
/// this wait.
/// </remarks>
internal sealed class RemoteUiInteractions(
	IHostInvoker invoker,
	ModalResultStore results,
	ILogger logger,
	string executeCorrelationId) : IUiInteractions
{
	private static readonly JsonSerializerOptions _valueOptions = new(JsonSerializerDefaults.Web);

	public async Task<bool> ShowModalAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
		=> await OpenAsync(originClientId, modal, awaitResult: false, cancellationToken).ConfigureAwait(false)
			is not null;

	public async Task<ModalResult<T>> ShowModalAsync<T>(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
	{
		var modalId = await OpenAsync(originClientId, modal, awaitResult: true, cancellationToken)
			.ConfigureAwait(false);
		if (modalId is null)
		{
			return ModalResult.FromCancellation<T>();
		}

		var answer = await results.Await(modalId, cancellationToken).ConfigureAwait(false);
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
			// The tree that produced this value is this plugin's own, so a payload that does not fit the
			// type it asked for is its bug - but it must not throw out of a running action.
			logger.HostCallbackFailed(Protocol.Callbacks.HostApis.ActionInteractions, "show-modal", exception);
			return ModalResult.FromCancellation<T>();
		}
	}

	private async Task<string?> OpenAsync(
		string? originClientId,
		ModalDefinition modal,
		bool awaitResult,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(modal);

		try
		{
			var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.ActionInteractions,
					HostOperations.ActionInteractions.ShowModal,
					new ActionInteractionsShowModalArguments
					{
						ExecuteCorrelationId = executeCorrelationId,
						OriginClientId = originClientId,
						ViewId = modal.ViewId,
						Title = modal.Title,
						Data = modal.Data,
						AwaitResult = awaitResult
					},
					cancellationToken)
				.ConfigureAwait(false);

			var opened = result?.Deserialize<ActionInteractionsShowModalResult>(PluginProtocolJson.Options);

			return opened is { Opened: true, ModalId.Length: > 0 } ? opened.ModalId : null;
		}
		catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
		{
			logger.HostCallbackFailed(Protocol.Callbacks.HostApis.ActionInteractions, "show-modal", exception);
			return null;
		}
	}
}
