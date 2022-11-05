using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fleck;
using MacroDeck.RPC.Enum;
using MacroDeck.RPC.Exceptions;
using MacroDeck.RPC;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Profiles;
using static System.Threading.Tasks.Task;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;
using System.Text.Json.Nodes;
using SuchByte.MacroDeck.Factories;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Server.V3;

namespace SuchByte.MacroDeck.Server;

public class MacroDeckServer : IObservable<RpcAction>
{
    public static MacroDeckServer Instance { get; } = new();

    public event EventHandler? OnDeviceConnectionStateChanged;
    public event EventHandler? OnServerStateChanged;
    public event EventHandler? OnFolderChanged;

    public WebSocketServer? WebSocketServer { get; private set; }

    public List<MacroDeckClient> Clients { get; } = new();
    private List<IObserver<RpcAction>> Observers { get; }

    private IRpcHandler _rpcHandler;
    private IRpcHandlerFactory _rpcHandlerFactory;
    private RpcDispatcher _rcpDispatcher;
    
    public MacroDeckServer()
    {
        // TODO: Add handler factory

        //Func<IRpcHandler> rpcHandlerFactory = () =>
        //{

        //}
        //_rpcHandlerFactory = new RpcHandlerFactory(handlers);
        //_rcpDispatcher = new RpcDispatcher(_rpcHandlerFactory);
    }

    public IDisposable Subscribe(IObserver<RpcAction> observer)
    {
        Observers.Add(observer);

        return new DisposeAction(delegate { Observers.Remove(observer); });
    }

    internal class DisposeAction : IDisposable
    {
        private readonly Action _disposeAction;

        public DisposeAction(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose() => _disposeAction();
    }

    public void Start(int port)
    {
        DeviceManager.LoadKnownDevices();
        StartWebSocketServer(IPAddress.Any.ToString(), port);
    }

    private void StartWebSocketServer(string ipAddress, int port)
    {
        if (WebSocketServer != null)
        {
            MacroDeckLogger.Info("Stopping websocket server...");
            foreach (var macroDeckClient in Clients.Where(macroDeckClient => macroDeckClient.SocketConnection is { IsAvailable: true }))
            {
                macroDeckClient.SocketConnection.Close();
            }
            WebSocketServer.Dispose();
            Clients.Clear();
            MacroDeckLogger.Info("Websocket server stopped");
            OnServerStateChanged?.Invoke(WebSocketServer, EventArgs.Empty);
        }
        MacroDeckLogger.Info($"Starting websocket server @ {ipAddress}:{port}");
        WebSocketServer = new WebSocketServer("ws://" + ipAddress + ":" + port);
        WebSocketServer.ListenerSocket.NoDelay = true;
        try
        {
            WebSocketServer.Start(socket =>
            {
                var macroDeckClient = new MacroDeckClient(socket);
                socket.OnOpen = () => OnOpen(macroDeckClient);
                socket.OnClose = () => OnClose(macroDeckClient);
                socket.OnError = delegate { OnClose(macroDeckClient); };
                socket.OnMessage = message => OnMessage(macroDeckClient, message);
            });
            OnServerStateChanged?.Invoke(WebSocketServer, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            OnServerStateChanged?.Invoke(WebSocketServer, EventArgs.Empty);

            MacroDeckLogger.Error("Failed to start server: " + ex.Message + Environment.NewLine + ex.StackTrace);

            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.Error, LanguageManager.Strings.FailedToStartServer + Environment.NewLine + ex.Message, MessageBoxButtons.OK);
        }
    }

    private void OnOpen(MacroDeckClient macroDeckClient)
    {
        if (MacroDeck.Configuration.BlockNewConnections ||
            Clients.Count >= 10 ||
            ProfileManager.CurrentProfile?.Folders.Count < 1)
        {
            CloseClient(macroDeckClient);
            return;
        }

        Clients.Add(macroDeckClient);
    }

    private void OnClose(MacroDeckClient macroDeckClient)
    {
        Clients.Remove(macroDeckClient);
        MacroDeckLogger.Info(macroDeckClient.ClientId + " connection closed");
        OnDeviceConnectionStateChanged?.Invoke(macroDeckClient, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the connection
    /// </summary>
    /// <param name="macroDeckClient"></param>
    public void CloseClient(MacroDeckClient macroDeckClient)
    {
        if (macroDeckClient?.SocketConnection == null ||
            !macroDeckClient.SocketConnection.IsAvailable) return;
        MacroDeckLogger.Info("Close connection to " + macroDeckClient.ClientId);
        macroDeckClient.SocketConnection.Close();
    }
        

    private void OnMessage(MacroDeckClient macroDeckClient, string jsonMessageString)
    {
        var responseObject = JObject.Parse(jsonMessageString);
        if (macroDeckClient.ProtocolVersion == DeviceProtocolVersion.Unknown)
        {
            if (responseObject["Method"] != null)
            {
                macroDeckClient.ProtocolVersion = DeviceProtocolVersion.V2;
            } else if (responseObject["jsonrpc"] != null)
            {
                macroDeckClient.ProtocolVersion = DeviceProtocolVersion.V3;
            }
        }

        switch (macroDeckClient.ProtocolVersion)
        {
            case DeviceProtocolVersion.V2:
                Task.Run(() => ProcessV2Async(macroDeckClient, responseObject));
                break;
            case DeviceProtocolVersion.V3:
                Task.Run(() => ProcessV3Async(macroDeckClient, jsonMessageString));
                break;
            case DeviceProtocolVersion.Unknown:
            default:
                // ignored
                break;
        }
    }

    private Task ProcessV3Async(MacroDeckClient macroDeckClient, string message)
    {
        return Run(() =>
        {
            try
            {
                var request = JsonSerializer.Deserialize<Request>(message);
                if (request == null) return;

                MacroDeckLogger.Trace($"Invoking Request! {request}");
                foreach (var observer in Observers)
                {
                    Task Callback(object? result)
                    {
                        var response = new Response { Id = request.Id, Result = result };
                        
                        macroDeckClient.SocketConnection?.Send(JsonSerializer.SerializeToUtf8Bytes(response));
                        return CompletedTask;
                    }

                    var actionable = new RpcAction(request, Callback);
                    observer.OnNext(actionable);
                }
            }
            catch (ActionException ae)
            {
                ReturnErrorFromException("Action error:", ae.Message, message, macroDeckClient, ae.Code);
            }
            catch (JsonException je)
            {
                ReturnErrorFromException("Json invalid:", je.Message, message, macroDeckClient, ErrorCode.JsonFormat);
            }
            catch (Exception ex)
            {
                ReturnErrorFromException("Unknown error occurred during message processing:", ex.Message, message, macroDeckClient, ErrorCode.Generic);
            }
        });
    }

    private static void ReturnErrorFromException(string exceptionMessageContext, string exceptionMessage, string message, MacroDeckClient macroDeckClient, ErrorCode errorCode = ErrorCode.Generic)
    {
        MacroDeckLogger.Error($"WebSocket Server Error: {exceptionMessageContext} {exceptionMessage}");
        var error = new Error(errorCode, $"{exceptionMessage}");
        string? id = null;
        try
        {
            var root = JsonNode.Parse(message);
            id = root?["Id"]?.ToString();
        }
        catch (Exception)
        {
            /* do nothing */
        }

        var response = new Response { Error = error, Id = id };
        macroDeckClient.SocketConnection?.Send(JsonSerializer.SerializeToUtf8Bytes(response));
    }

    private Task ProcessV2Async(MacroDeckClient macroDeckClient, JObject responseObject)
    {
        return Run(() =>
        {
            if (responseObject["Method"] == null) return;

            if (!Enum.TryParse(typeof(JsonMethod), responseObject["Method"].ToString(), out var method)) return;

            MacroDeckLogger.Trace("Received method: " + method);

            switch (method)
            {
                case JsonMethod.CONNECTED:
                    if (responseObject["API"] == null || responseObject["Client-Id"] == null || responseObject["Device-Type"] == null || responseObject["API"].ToObject<int>() < MacroDeck.ApiVersion)
                    {
                        CloseClient(macroDeckClient);
                        return;
                    }

                    macroDeckClient.SetClientId(responseObject["Client-Id"].ToString());

                    MacroDeckLogger.Info("Connection request from " + macroDeckClient.ClientId);

                    var deviceType = DeviceType.Unknown;
                    Enum.TryParse(responseObject["Device-Type"].ToString(), out deviceType);
                    macroDeckClient.DeviceType = deviceType;

                    if (!DeviceManager.RequestConnection(macroDeckClient))
                    {
                        CloseClient(macroDeckClient);
                        return;
                    }

                    if (!macroDeckClient.SocketConnection.IsAvailable || DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId) == null)
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId))
                    {
                        DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId = ProfileManager.Profiles.FirstOrDefault().ProfileId;
                    }

                    DeviceManager.SaveKnownDevices();

                    macroDeckClient.Profile = ProfileManager.FindProfileById(DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId);

                    if (macroDeckClient.Profile == null)
                    {
                        macroDeckClient.Profile = ProfileManager.Profiles.FirstOrDefault();
                    }

                    macroDeckClient.Folder = macroDeckClient.Profile.Folders.FirstOrDefault();

                    macroDeckClient.DeviceMessage.Connected(macroDeckClient);


                    OnDeviceConnectionStateChanged?.Invoke(macroDeckClient, EventArgs.Empty);
                    MacroDeckLogger.Info(macroDeckClient.ClientId + " connected");
                    break;
                case JsonMethod.BUTTON_PRESS:
                case JsonMethod.BUTTON_RELEASE:
                case JsonMethod.BUTTON_LONG_PRESS:
                case JsonMethod.BUTTON_LONG_PRESS_RELEASE:
                    var buttonPressType = method switch
                    {
                        JsonMethod.BUTTON_PRESS => ButtonPressType.SHORT,
                        JsonMethod.BUTTON_RELEASE => ButtonPressType.SHORT_RELEASE,
                        JsonMethod.BUTTON_LONG_PRESS => ButtonPressType.LONG,
                        JsonMethod.BUTTON_LONG_PRESS_RELEASE => ButtonPressType.LONG_RELEASE,
                        _ => ButtonPressType.SHORT
                    };

                    try
                    {
                        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
                        var row = int.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        var column = int.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        var actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            Execute(actionButton, macroDeckClient.ClientId, buttonPressType);
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Action button long press release caused an exception: " + ex.Message);
                    }
                    break;
                case JsonMethod.GET_BUTTONS:
                    Task.Run(() =>
                    {
                        SendAllButtons(macroDeckClient);
                    });
                    break;
            }
        });
    }

    internal void Execute(ActionButton.ActionButton actionButton, string clientId, ButtonPressType buttonPressType)
    {
        var actions = buttonPressType switch
        {
            ButtonPressType.SHORT => actionButton.Actions,
            ButtonPressType.SHORT_RELEASE => actionButton.ActionsRelease,
            ButtonPressType.LONG => actionButton.ActionsLongPress,
            ButtonPressType.LONG_RELEASE => actionButton.ActionsLongPressRelease,
            _ => actionButton.Actions
        };

        Task.Run(() =>
        {
            foreach (var action in actions)
            {
                try
                {
                    action.Trigger(clientId, actionButton);
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Sets the current profile of a client
    /// </summary>
    /// <param name="macroDeckClient"></param>
    /// <param name="macroDeckProfile"></param>
    public void SetProfile(MacroDeckClient macroDeckClient, MacroDeckProfile macroDeckProfile)
    {
        macroDeckClient.Profile = macroDeckProfile;
        macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
            
        SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
    }

    /// <summary>
    /// Sets the current folder of a client
    /// </summary>
    /// <param name="macroDeckClient"></param>
    /// <param name="folder"></param>
    public void SetFolder(MacroDeckClient macroDeckClient, MacroDeckFolder folder)
    {
        macroDeckClient.Folder = folder;
        SendAllButtons(macroDeckClient);
        OnFolderChanged?.Invoke(macroDeckClient, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the folder on all clients with this folder as the current folder
    /// </summary>
    /// <param name="folder"></param>
    public void UpdateFolder(MacroDeckFolder folder)
    {
        foreach (var macroDeckClient in Clients.FindAll(macroDeckClient => macroDeckClient.Folder.Equals(folder)))
        {
            SendAllButtons(macroDeckClient);
        }
    }

    /// <summary>
    /// Sends all buttons of the current folder to the client
    /// </summary>
    /// <param name="macroDeckClient"></param>
    private void SendAllButtons(MacroDeckClient macroDeckClient)
    {
        macroDeckClient?.DeviceMessage?.SendAllButtons(macroDeckClient);
    }

    /// <summary>
    /// Sends a single button to the client
    /// </summary>
    /// <param name="macroDeckClient"></param>
    /// <param name="actionButton"></param>
    public void SendButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
    {
        macroDeckClient?.DeviceMessage?.UpdateButton(macroDeckClient, actionButton);
    }

    /// <summary>
    /// Set button state on or off
    /// </summary>
    /// <param name="actionButton">ActionButton</param>
    /// <param name="state">State true = on, off = false</param>
    public void SetState(ActionButton.ActionButton actionButton, bool state)
    {
        actionButton.State = state;
    }

    internal void UpdateState(ActionButton.ActionButton actionButton)
    {
        foreach (var macroDeckClient in Clients.FindAll(macroDeckClient =>
                     macroDeckClient.Folder.ActionButtons.Contains(actionButton)))
        {
            SendButton(macroDeckClient, actionButton);
        }
    }

    /// <summary>
    /// Get the MacroDeckClient from the client id
    /// </summary>
    /// <param name="macroDeckClientId">Client-ID</param>
    /// <returns></returns>
    public MacroDeckClient? GetMacroDeckClient(string macroDeckClientId)
    {
        return string.IsNullOrWhiteSpace(macroDeckClientId) ? null : Clients.Find(macroDeckClient => macroDeckClient.ClientId == macroDeckClientId);
    }

    /// <summary>
    /// Raw send function
    /// </summary>
    /// <param name="socketConnection"></param>
    /// <param name="jObject"></param>
    internal void Send(IWebSocketConnection socketConnection, JObject jObject)
    {
        socketConnection.Send(jObject.ToString());
    }
}