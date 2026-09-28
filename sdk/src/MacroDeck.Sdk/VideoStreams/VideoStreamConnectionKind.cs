namespace MacroDeck.Sdk.VideoStreams;

/// <summary>How a consumer is connected to Macro Deck.</summary>
public enum VideoStreamConnectionKind
{
	/// <summary>On the same computer as Macro Deck: a loopback address reaches the provider.</summary>
	Local,

	/// <summary>Over the network: the consumer needs an address it can reach on the network.</summary>
	Network,

	/// <summary>
	/// A device tethered by USB whose traffic Macro Deck tunnels. It connects through a loopback address
	/// on the device, but that address is the device's own, so a <c>localhost</c> URL does not reach the
	/// computer; only ports Macro Deck tunnels do.
	/// </summary>
	UsbTunnel
}
