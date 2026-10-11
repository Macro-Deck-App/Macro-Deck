using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IUiEventOrigin" /> to pass to
/// <see cref="IUiSession.Dispatch(MacroDeck.Ui.Model.Events.UiEvent, IUiEventOrigin)" /> in a test. Records
/// every failure and dialog the session asked for. <see cref="IsAttached" /> models the client still being
/// attached to the session: set it to <c>false</c> and every call answers as Macro Deck does for a client that
/// went away.
/// </summary>
public sealed class FakeUiEventOrigin : IUiEventOrigin
{
	private static readonly JsonSerializerOptions _valueOptions = new(JsonSerializerDefaults.Web);

	private readonly Lock _gate = new();
	private readonly List<LocalizedText> _failures = [];
	private readonly List<ModalDefinition> _modals = [];

	/// <summary>Creates an origin for <paramref name="clientId" />.</summary>
	public FakeUiEventOrigin(string? clientId = "test-client") => ClientId = clientId;

	/// <inheritdoc />
	public string? ClientId { get; }

	/// <summary>Whether the client still counts as attached to the session. Defaults to <c>true</c>.</summary>
	public bool IsAttached { get; set; } = true;

	/// <summary>
	/// What a dialog opened through <see cref="ShowModalAsync{T}" /> answers, as the JSON value the dialog's
	/// tree completed with. <c>null</c>, the default, answers a cancellation.
	/// </summary>
	public Func<ModalDefinition, JsonElement?>? ModalAnswer { get; set; }

	/// <summary>Every failure shown so far, in call order.</summary>
	public IReadOnlyList<LocalizedText> ReportedFailures
	{
		get
		{
			lock (_gate)
			{
				return [.. _failures];
			}
		}
	}

	/// <summary>Every dialog opened so far, in call order.</summary>
	public IReadOnlyList<ModalDefinition> OpenedModals
	{
		get
		{
			lock (_gate)
			{
				return [.. _modals];
			}
		}
	}

	/// <inheritdoc />
	public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!CanReach())
		{
			return Task.FromResult(false);
		}

		lock (_gate)
		{
			_failures.Add(message);
		}

		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modal);
		cancellationToken.ThrowIfCancellationRequested();

		return Task.FromResult(TryOpen(modal));
	}

	/// <inheritdoc />
	public Task<ModalResult<T>> ShowModalAsync<T>(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modal);
		cancellationToken.ThrowIfCancellationRequested();

		if (!TryOpen(modal) || ModalAnswer?.Invoke(modal) is not { } answer)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}

		return Task.FromResult(ModalResult.FromValue(answer.Deserialize<T>(_valueOptions)));
	}

	private bool TryOpen(ModalDefinition modal)
	{
		if (!CanReach())
		{
			return false;
		}

		lock (_gate)
		{
			_modals.Add(modal);
		}

		return true;
	}

	private bool CanReach() => IsAttached && !string.IsNullOrEmpty(ClientId);
}
