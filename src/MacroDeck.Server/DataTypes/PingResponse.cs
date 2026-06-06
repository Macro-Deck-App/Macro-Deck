namespace MacroDeck.Server.DataTypes;

public class PingResponse
{
    public string MachineName { get; set; }

    public PingResponse()
    {
        MachineName = Environment.MachineName;
    }
}