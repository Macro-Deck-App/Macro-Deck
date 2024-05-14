using System.Net;
using MacroDeck.Server;
using MacroDeck.Server.DataTypes;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Configuration;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Server;

public static class MacroDeckServer
{
    public static event EventHandler? OnDeviceConnectionStateChanged;
    public static event EventHandler? OnServerStateChanged;
    public static event EventHandler? OnFolderChanged;

    public static List<MacroDeckClient> Clients { get; } = new();

    public static string QuickSetupToken { get; } = RandomStringGenerator.RandomString(8);
    
    public static void Start(int port)
    {
        DeviceManager.LoadKnownDevices();
        Task.Run(async () => await StartWebSocketServer(port));
    }

    private static async Task StartWebSocketServer(int port)
    {
        Clients.Clear();
        WebSocketHandler.SessionDisconnected += WebSocketHandlerOnSessionDisconnected;
        WebSocketHandler.SessionConnected += WebSocketHandlerOnSessionConnected;
        WebSocketHandler.MessageReceived += WebSocketHandlerOnMessageReceived;
        var enableSsl = MacroDeck.Configuration.EnableSsl;
        var certificatePath = MacroDeck.Configuration.SslCertificatePath;
        var certificatePassword = MacroDeck.Configuration.SslCertificatePassword;
        try
        {
            await MacroDeckServerHelper.Setup(port, enableSsl, certificatePath, certificatePassword);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error("Failed to start server: " + ex.Message + Environment.NewLine + ex.StackTrace);

            using var msgBox = new GUI.CustomControls.MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.Error, LanguageManager.Strings.FailedToStartServer + Environment.NewLine + ex.Message, MessageBoxButtons.OK);
        }
        
    }

    private static void WebSocketHandlerOnMessageReceived(object? sender, string message)
    {
        if (sender is not WebSocketSession session)
        {
            return;
        }

        var sessionId = session.Id;
        var macroDeckClient = Clients.FirstOrDefault(x => x.SessionId == sessionId);
        if (macroDeckClient is null)
        {
            return;
        }
        
        OnMessage(macroDeckClient, message);
    }

    private static void WebSocketHandlerOnSessionConnected(object? sender, EventArgs e)
    {
        if (sender is not WebSocketSession session)
        {
            return;
        }
        
        var macroDeckClient = new MacroDeckClient(session.Id);
        if (MacroDeck.Configuration.BlockNewConnections ||
            Clients.Count >= 10 ||
            ProfileManager.CurrentProfile?.Folders.Count < 1)
        {
            CloseClient(macroDeckClient);
            return;
        }

        Clients.Add(macroDeckClient);
    }

    private static void WebSocketHandlerOnSessionDisconnected(object? sender, EventArgs e)
    {
        if (sender is not WebSocketSession session)
        {
            return;
        }

        var sessionId = session.Id;
        var macroDeckClient = Clients.FirstOrDefault(x => x.SessionId == sessionId);
        if (macroDeckClient is null)
        {
            return;
        }
        
        Clients.Remove(macroDeckClient);
        MacroDeckLogger.Info(macroDeckClient.ClientId + " connection closed");
        OnDeviceConnectionStateChanged?.Invoke(macroDeckClient, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the connection
    /// </summary>
    /// <param name="macroDeckClient"></param>
    public static void CloseClient(MacroDeckClient macroDeckClient)
    {
        MacroDeckLogger.Info("Close connection to " + macroDeckClient.ClientId);
        Task.Run(async () => await WebSocketHandler.Close(macroDeckClient.SessionId));
    }
        

    private static void OnMessage(MacroDeckClient macroDeckClient, string jsonMessageString)
    {
        var responseObject = JObject.Parse(jsonMessageString);

        if (responseObject["Method"] == null) return;

        if (!Enum.TryParse(typeof(JsonMethod), responseObject["Method"].ToString(), out var method)) return;

        MacroDeckLogger.Trace("Received method: " + method);

        switch (method)
        {
            case JsonMethod.CONNECTED:
                if (responseObject["API"] == null || responseObject["Client-Id"] == null ||
                    responseObject["Device-Type"] == null ||
                    responseObject["API"].ToObject<int>() < MacroDeck.ApiVersion)
                {
                    CloseClient(macroDeckClient);
                    return;
                }

                macroDeckClient.SetClientId(responseObject["Client-Id"].ToString());

                MacroDeckLogger.Info("Connection request from " + macroDeckClient.ClientId);

                Enum.TryParse(responseObject["Device-Type"].ToString(), out DeviceType deviceType);
                macroDeckClient.DeviceType = deviceType;

                if (responseObject["Token"]?.ToString() is { } token && token.EqualsCryptographically(QuickSetupToken))
                {
                    DeviceManager.AddKnownDevice(macroDeckClient);
                }
                else
                {
                    if (!DeviceManager.RequestConnection(macroDeckClient))
                    {
                        CloseClient(macroDeckClient);
                        return;
                    }
                }
                
                if (DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId) == null)
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
    }

    internal static void Execute(ActionButton.ActionButton actionButton, string clientId, ButtonPressType buttonPressType)
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
                if (action is null)
                {
                    continue;
                }
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
    public static void SetProfile(MacroDeckClient macroDeckClient, MacroDeckProfile macroDeckProfile)
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
    public static void SetFolder(MacroDeckClient macroDeckClient, MacroDeckFolder folder)
    {
        macroDeckClient.Folder = folder;
        SendAllButtons(macroDeckClient);
        OnFolderChanged?.Invoke(macroDeckClient, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the folder on all clients with this folder as the current folder
    /// </summary>
    /// <param name="folder"></param>
    public static void UpdateFolder(MacroDeckFolder folder)
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
    private static void SendAllButtons(MacroDeckClient macroDeckClient)
    {
        macroDeckClient?.DeviceMessage?.SendAllButtons(macroDeckClient);
    }

    /// <summary>
    /// Sends a single button to the client
    /// </summary>
    /// <param name="macroDeckClient"></param>
    /// <param name="actionButton"></param>
    public static void SendButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
    {
        macroDeckClient?.DeviceMessage?.UpdateButton(macroDeckClient, actionButton);
    }

    /// <summary>
    /// Set button state on or off
    /// </summary>
    /// <param name="actionButton">ActionButton</param>
    /// <param name="state">State true = on, off = false</param>
    public static void SetState(ActionButton.ActionButton actionButton, bool state)
    {
        actionButton.State = state;
    }

    internal static void UpdateState(ActionButton.ActionButton actionButton)
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
    public static MacroDeckClient? GetMacroDeckClient(string macroDeckClientId)
    {
        return string.IsNullOrWhiteSpace(macroDeckClientId) ? null : Clients.Find(macroDeckClient => macroDeckClient.ClientId == macroDeckClientId);
    }

    /// <summary>
    /// Raw send function
    /// </summary>
    /// <param name="macroDeckClient"></param>
    /// <param name="jObject"></param>
    internal static void Send(MacroDeckClient macroDeckClient, JObject jObject)
    {
        Task.Run(async () => await WebSocketHandler.SendMessageToClient(macroDeckClient.SessionId, jObject.ToString()));
    }
}