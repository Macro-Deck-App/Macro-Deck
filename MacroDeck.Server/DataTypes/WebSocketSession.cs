using System.Net.WebSockets;
using System.Text;

namespace MacroDeck.Server.DataTypes;

public class WebSocketSession : IDisposable
{
    private readonly WebSocket _webSocket;
    public string Id { get; }

    public event EventHandler<string>? TextMessageReceived;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? Error;

    public WebSocketSession(WebSocket webSocket)
    {
        _webSocket = webSocket;
        Id = Guid.NewGuid().ToString();
    }

    internal async Task Start()
    {
        await Task.Run(DoWork);
    }

    private async Task DoWork()
    {
        try
        {
            while (_webSocket.State is WebSocketState.Open)
            {
                var message = await ReceiveStringAsync();
                if (message == null)
                {
                    return;
                }

                TextMessageReceived?.Invoke(this, message);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
        finally
        {
            await Close();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
    
    private async Task<string?> ReceiveStringAsync()
    {
        var buffer = new byte[1024 * 4];
        var receivedMessage = new StringBuilder();
        
        WebSocketReceiveResult result;
        do
        {
            if (_webSocket.State == WebSocketState.CloseReceived)
            {
                return null;
            }
            
            var arraySegment = new ArraySegment<byte>(buffer);
            result = await _webSocket.ReceiveAsync(arraySegment, CancellationToken.None);
            
            if (result.MessageType is not WebSocketMessageType.Text and not WebSocketMessageType.Close)
            {
                throw new InvalidOperationException();
            }

            receivedMessage.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return receivedMessage.ToString();
    }

    public async Task Close()
    {
        await Close(new WebSocketNormalClose());
    }

    public async Task Close(WebSocketCloseReason reason)
    {
        await Close(reason.Status, reason.Description);
    }

    private async Task Close(WebSocketCloseStatus webSocketCloseStatus, string statusDescription)
    {
        if (_webSocket.State is not WebSocketState.Open)
        {
            return;
        }

        await _webSocket.CloseAsync(
            webSocketCloseStatus,
            statusDescription,
            CancellationToken.None);
    }

    public async Task SendMessage(string message)
    {
        await SendMessage(Encoding.UTF8.GetBytes(message));
    }
    
    private async Task SendMessage(byte[] data)
    {
        await _webSocket.SendAsync(
            new ArraySegment<byte>(data),
            WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage,
            CancellationToken.None);
    }

    public async void Dispose()
    {
        await Close();
        _webSocket.Dispose();
    }
}