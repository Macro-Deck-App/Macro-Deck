using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>Spy over <see cref="IWidgetRenderSignals" /> that records every raised call verbatim, so a
/// test can assert both that a call site actually raised its signal and that the payload it raised is
/// exactly what the same call site pushed to clients.</summary>
internal sealed class RecordingRenderSignals : IWidgetRenderSignals
{
	public List<WidgetStateUpdatedEvent> StateChanges { get; } = [];
	public List<LabelTextUpdatedEvent> LabelChanges { get; } = [];
	public List<WidgetEntity> DataChanges { get; } = [];
	public List<Guid> IconInvalidations { get; } = [];
	public List<Guid> WidgetIconChanges { get; } = [];
	public int VariableChanges { get; private set; }

	public IDisposable SubscribeState(string widgetId, Action<WidgetStateUpdatedEvent> handler) =>
		NoopDisposable.Instance;

	public IDisposable SubscribeLabel(string widgetId, Action<LabelTextUpdatedEvent> handler) =>
		NoopDisposable.Instance;

	public IDisposable SubscribeDataChanged(string widgetId, Action<WidgetEntity> handler) => NoopDisposable.Instance;

	public IDisposable SubscribeIconInvalidated(Action<Guid> handler) => NoopDisposable.Instance;

	public IDisposable SubscribeWidgetIconChanged(string widgetId, Action handler) => NoopDisposable.Instance;

	public IDisposable SubscribeVariableChanged(Action handler) => NoopDisposable.Instance;

	public void RaiseStateChanged(WidgetStateUpdatedEvent evt) => StateChanges.Add(evt);

	public void RaiseLabelChanged(LabelTextUpdatedEvent evt) => LabelChanges.Add(evt);

	/// <summary>Records the raise and reports it taken, so a caller that falls back when nothing handled a
	/// change does not take that path here.</summary>
	public bool RaiseDataChanged(WidgetEntity widget)
	{
		DataChanges.Add(widget);

		return true;
	}

	public void RaiseIconInvalidated(Guid iconId) => IconInvalidations.Add(iconId);

	public void RaiseWidgetIconChanged(Guid widgetId) => WidgetIconChanges.Add(widgetId);

	public void RaiseVariableChanged() => VariableChanges++;

	private sealed class NoopDisposable : IDisposable
	{
		public static readonly NoopDisposable Instance = new();

		public void Dispose()
		{
		}
	}
}
