using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IWidgetApi" />. Seed the widgets that "exist" with <see cref="Seed" />;
/// <see cref="ApplyAsync" /> reports <c>false</c> - never throws - for an unknown widget id or a request
/// that carries nothing to do, matching the interface's own no-op contract, and records what a real change
/// applied so a test can inspect it via <see cref="LastApplied" />.
/// </summary>
public sealed class FakeWidgetApi : IWidgetApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, WidgetTargetInfo> _widgets = new(StringComparer.Ordinal);
	private readonly Dictionary<string, WidgetAppearanceRequest> _lastApplied = new(StringComparer.Ordinal);

	/// <summary>
	/// The most recent <see cref="ApplyAsync" /> request that actually changed something, by widget id.
	/// </summary>
	public IReadOnlyDictionary<string, WidgetAppearanceRequest> LastApplied
	{
		get
		{
			lock (_gate)
			{
				return new Dictionary<string, WidgetAppearanceRequest>(_lastApplied, StringComparer.Ordinal);
			}
		}
	}

	/// <summary>
	/// Adds (or replaces) a widget so <see cref="Exists" />, <see cref="GetWidgets" /> and
	/// <see cref="ApplyAsync" /> can see it.
	/// </summary>
	public void Seed(WidgetTargetInfo widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		lock (_gate)
		{
			_widgets[widget.Id] = widget;
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<WidgetTargetInfo> GetWidgets()
	{
		lock (_gate)
		{
			return [.. _widgets.Values];
		}
	}

	/// <inheritdoc />
	public bool Exists(string widgetId)
	{
		lock (_gate)
		{
			return _widgets.ContainsKey(widgetId);
		}
	}

	/// <summary>
	/// Applies a patch, or reports <c>false</c> without throwing when <paramref name="request" /> targets
	/// an unseeded widget id or asks for nothing - an empty <see cref="WidgetAppearancePatch" /> *and* no
	/// <see cref="WidgetAppearanceRequest.ClearProperties" /> - the same no-op contract
	/// <see cref="IWidgetApi.ApplyAsync" /> documents for a widget deleted out from under a flow. A
	/// clear-only request is a real change, like it is on the host. A real change is recorded into
	/// <see cref="LastApplied" /> and reported as <c>true</c>.
	/// </summary>
	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
	{
		if (request is null)
		{
			return Task.FromResult(false);
		}

		lock (_gate)
		{
			if (!_widgets.ContainsKey(request.WidgetId) ||
				(request.Patch.IsEmpty && request.ClearProperties.Count == 0))
			{
				return Task.FromResult(false);
			}

			_lastApplied[request.WidgetId] = request;
			return Task.FromResult(true);
		}
	}

	/// <inheritdoc />
	public Task<WidgetStateWriteResult> SetStateAsync(
		string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (!_widgets.TryGetValue(widgetId, out var widget))
			{
				return Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
			}

			if (!widget.States.Any(state => string.Equals(state.Id, stateId, StringComparison.Ordinal)))
			{
				return Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.UnknownState));
			}

			_widgets[widgetId] = WithCurrentState(widget, stateId);
			return Task.FromResult(WidgetStateWriteResult.Succeeded(stateId));
		}
	}

	/// <inheritdoc />
	public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
		CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (!_widgets.TryGetValue(widgetId, out var widget) || widget.States.Count == 0)
			{
				return Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
			}

			var ids = widget.States.Select(state => state.Id).ToList();
			var index = widget.CurrentStateId is not null ? ids.IndexOf(widget.CurrentStateId) : -1;
			var next = ids[(index + 1) % ids.Count];
			_widgets[widgetId] = WithCurrentState(widget, next);
			return Task.FromResult(WidgetStateWriteResult.Succeeded(next));
		}
	}

	private static WidgetTargetInfo WithCurrentState(WidgetTargetInfo widget, string currentStateId)
		=> new()
		{
			Id = widget.Id,
			Label = widget.Label,
			Location = widget.Location,
			Type = widget.Type,
#pragma warning disable CS0618 // Carried through verbatim; not this fake's job to derive it.
			HasOnOffStates = widget.HasOnOffStates,
#pragma warning restore CS0618
			States = widget.States,
			CurrentStateId = currentStateId,
			AppearanceProperties = widget.AppearanceProperties
		};
}
