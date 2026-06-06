using System.Net.WebSockets;
using MacroDeck.Server.DataTypes;

namespace MacroDeck.Server;

public class WebSocketHandler
{
    public static event EventHandler<string>? MessageReceived;
    public static event EventHandler? SessionConnected; 
    public static event EventHandler? SessionDisconnected; 

    private static List<WebSocketSession> ClientSessions { get; } = new();

    public static async Task SendMessageToAll(string message)
    {
        await SendMessageToMany(ClientSessions, message);
    }

    public static async Task SendMessageToMany(IList<WebSocketSession> webSocketSessions, string message)
    {
        var tasks = webSocketSessions.Select(session => session.SendMessage(message))
            .ToList();
        await Task.WhenAll(tasks);
    }

    public static async Task SendMessageToClient(string id, string message)
    {
        var session = ClientSessions.FirstOrDefault(x => x.Id.Equals(id));
        if (session == null)
        {
            return;
        }

        await session.SendMessage(message);
    }

    internal static async Task HandleWebSocket(WebSocket webSocket)
    {
        var session = new WebSocketSession(webSocket);
        session.TextMessageReceived += SessionOnTextMessageReceived;
        session.Disconnected += SessionOnDisconnected;
        lock (ClientSessions)
        {
            ClientSessions.Add(session);
        }

        SessionConnected?.Invoke(session, EventArgs.Empty);
        await session.Start();
    }
    
    private static void SessionOnDisconnected(object? sender, EventArgs e)
    {
        if (sender is not WebSocketSession session)
        {
            return;
        }
        
        SessionDisconnected?.Invoke(session, EventArgs.Empty);
        
        session.TextMessageReceived -= SessionOnTextMessageReceived;
        session.Disconnected -= SessionOnDisconnected;
        session.Dispose();
        ClientSessions.Remove(session);
    }

    private static void SessionOnTextMessageReceived(object? sender, string message)
    {
        if (sender is not WebSocketSession session)
        {
            return;
        }
        
        MessageReceived?.Invoke(session, message);
    }

    public static async Task Close(string sessionId)
    {
        var session = ClientSessions.FirstOrDefault(x => x.Id == sessionId);
        if (session is null)
        {
            return;
        }

        await session.Close();
    }

    public static bool IsAvailable(string sessionId)
    {
        return ClientSessions.Any(x => x.Id == sessionId);
    }
}