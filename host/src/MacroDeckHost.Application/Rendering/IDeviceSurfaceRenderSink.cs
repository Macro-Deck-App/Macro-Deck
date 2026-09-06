namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// Where a widget state or label push goes for the device sessions rendering that widget, alongside the
/// realtime group send that serves connected clients. Optional everywhere it is consumed: a host with no
/// device sessions has nothing to notify.
/// </summary>
public interface IDeviceSurfaceRenderSink
{
	Task OnWidgetStateChanged(Guid widgetId, CancellationToken cancellationToken = default);

	Task OnWidgetLabelChanged(Guid widgetId, string state, CancellationToken cancellationToken = default);

	/// <summary>Raised whenever a widget's icon-provider resolution changes - see
	/// <c>IWidgetRenderSignals.RaiseWidgetIconChanged</c> - so a device session rendering that widget is
	/// rebuilt and re-pushed the same way a state or label change already is.</summary>
	Task OnWidgetIconChanged(Guid widgetId, CancellationToken cancellationToken = default);
}
