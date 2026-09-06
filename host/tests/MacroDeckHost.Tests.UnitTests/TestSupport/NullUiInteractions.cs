using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Modals;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>
/// The modal surface for a run with no client to ask: every modal is refused, so a test exercising a flow
/// sees exactly what a backend-initiated execution sees.
/// </summary>
internal sealed class NullUiInteractions : IUiInteractions, IUiInteractionsFactory
{
	public IUiInteractions ForIntegration(string integrationId) => this;

	public Task<bool> ShowModalAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(false);

	public Task<ModalResult<T>> ShowModalAsync<T>(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(ModalResult.FromCancellation<T>());
}
