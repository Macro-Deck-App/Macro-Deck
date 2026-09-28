using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Modals;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingUiInteractions : IUiInteractionsFactory, IUiInteractions
{
	public TaskCompletionSource<(string? Client, ModalDefinition Modal)> Opened { get; }
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	public IUiInteractions ForIntegration(string integrationId) => this;

	public Task<bool> ShowModalAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
	{
		Opened.TrySetResult((originClientId, modal));
		return Task.FromResult(true);
	}

	public Task<ModalResult<T>> ShowModalAsync<T>(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(ModalResult.FromCancellation<T>());
}
