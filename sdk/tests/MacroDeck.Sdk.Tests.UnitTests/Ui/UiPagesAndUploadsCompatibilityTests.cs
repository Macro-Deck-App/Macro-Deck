using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Sdk.Tests.UnitTests.Ui;

[TestFixture]
public class UiPagesAndUploadsCompatibilityTests
{
	[Test]
	public void A_provider_written_before_pages_declares_none()
	{
		IUiProvider provider = new ProviderWrittenBeforePages();

		Assert.That(provider.Pages, Is.Empty);
	}

	[Test]
	public void An_origin_written_before_uploads_reports_that_it_cannot_read_one()
	{
		IUiEventOrigin origin = new OriginWrittenBeforeUploads();

		Assert.ThrowsAsync<NotSupportedException>(() => origin.ReadUploadAsync("upload-1"));
	}

	private sealed class ProviderWrittenBeforePages : IUiProvider
	{
		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(null);
		}
	}

	private sealed class OriginWrittenBeforeUploads : IUiEventOrigin
	{
		public string? ClientId => "client-a";

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(true);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(true);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}
}
