namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class ConnectionEndpointDto
{
	public string Address { get; set; } = string.Empty;

	public int Port { get; set; }

	public bool Ssl { get; set; }
}

public class GetConnectionInfoResponse
{
	public string InstanceName { get; set; } = string.Empty;

	public List<ConnectionEndpointDto> Endpoints { get; set; } = [];

	public bool PublicListenerUnavailable { get; set; }

	public string Version { get; set; } = string.Empty;
}
