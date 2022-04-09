using Fleck;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Plugins;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Profiles;
using System.Diagnostics;
using SuchByte.MacroDeck.Icons;
using System.Linq;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Server
{
    public static class MacroDeckServer
    {
        public static event EventHandler OnDeviceConnectionStateChanged;
        public static event EventHandler OnServerStateChanged;
        public static event EventHandler OnFolderChanged;

        private static WebSocketServer _webSocketServer;
        private static WebSocketServer _usbWebSocketServer;

        public static WebSocketServer WebSocketServer { get { return _webSocketServer; } }

        public static WebSocketServer USBWebSocketServer { get { return _usbWebSocketServer; } }


        private static readonly List<MacroDeckClient> _clients = new List<MacroDeckClient>();
        public static List<MacroDeckClient> Clients { get { return _clients; } }

        /// <summary>
        /// Starts the websocket server
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// 
        public static void Start(string ipAddress, int port)
        {
            StartWebSocketServer(ipAddress, port);
            StartUSBWebSocketServer("127.0.0.1", port);
        }

        private static void StartUSBWebSocketServer(string ipAddress, int port)
        {
            DeviceManager.LoadKnownDevices();
            Thread.Sleep(100);
            if (_usbWebSocketServer != null)
            {
                MacroDeckLogger.Info("Stopping USB websocket server...");
                foreach (MacroDeckClient macroDeckClient in _clients)
                {
                    if (macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
                    {
                        macroDeckClient.SocketConnection.Close();
                    }
                }
                _usbWebSocketServer.Dispose();
                MacroDeckLogger.Info("USB websocket server stopped");
                if (OnServerStateChanged != null)
                {
                    OnServerStateChanged(_webSocketServer, EventArgs.Empty);
                }

            }
            MacroDeckLogger.Info(string.Format("Starting USB websocket server @ {0}:{1}", ipAddress, port));
            _usbWebSocketServer = new WebSocketServer("ws://" + ipAddress + ":" + port);
            _usbWebSocketServer.ListenerSocket.NoDelay = true;
            try
            {
                _usbWebSocketServer.Start(socket =>
                {
                    MacroDeckClient macroDeckClient = new MacroDeckClient(socket);
                    socket.OnOpen = () => OnOpen(macroDeckClient);
                    socket.OnClose = () => OnClose(macroDeckClient);
                    socket.OnError = delegate (Exception ex) { OnClose(macroDeckClient); };
                    socket.OnMessage = message => OnMessage(macroDeckClient, message);
                });
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Failed to start USB server: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        private static void StartWebSocketServer(string ipAddress, int port)
        {
            DeviceManager.LoadKnownDevices();
            Thread.Sleep(100);
            if (_webSocketServer != null)
            {
                MacroDeckLogger.Info("Stopping websocket server...");
                foreach (MacroDeckClient macroDeckClient in _clients)
                {
                    if (macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
                    {
                        macroDeckClient.SocketConnection.Close();
                    }
                }
                _webSocketServer.Dispose();
                _clients.Clear();
                MacroDeckLogger.Info("Websocket server stopped");
                if (OnServerStateChanged != null)
                {
                    OnServerStateChanged(_webSocketServer, EventArgs.Empty);
                }
                
            }
            MacroDeckLogger.Info(String.Format("Starting websocket server @ {0}:{1}", ipAddress, port));
            _webSocketServer = new WebSocketServer("ws://" + ipAddress + ":" + port);
            _webSocketServer.ListenerSocket.NoDelay = true;
            try
            {
                _webSocketServer.Start(socket =>
                {
                    MacroDeckClient macroDeckClient = new MacroDeckClient(socket);
                    socket.OnOpen = () => OnOpen(macroDeckClient);
                    socket.OnClose = () => OnClose(macroDeckClient);
                    socket.OnError = delegate (Exception ex) { OnClose(macroDeckClient); };
                    socket.OnMessage = message => OnMessage(macroDeckClient, message);
                });
                if (OnServerStateChanged != null)
                {
                    OnServerStateChanged(_webSocketServer, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                if (OnServerStateChanged != null)
                {
                    OnServerStateChanged(_webSocketServer, EventArgs.Empty);
                }

                MacroDeckLogger.Error("Failed to start server: " + ex.Message + Environment.NewLine + ex.StackTrace);

                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.FailedToStartServer + Environment.NewLine + ex.Message, MessageBoxButtons.OK);
                }
            }
        }

        private static void OnOpen(MacroDeckClient macroDeckClient)
        {
            if (MacroDeck.Configuration.BlockNewConnections ||
                Clients.Count >= 10 ||
                ProfileManager.CurrentProfile.Folders == null || 
                ProfileManager.CurrentProfile.Folders.Count < 1)
            {
                CloseClient(macroDeckClient);
                return;
            }

            _clients.Add(macroDeckClient);
        }

        private static void OnClose(MacroDeckClient macroDeckClient)
        {
            macroDeckClient.Dispose();
            _clients.Remove(macroDeckClient);
            MacroDeckLogger.Info(macroDeckClient.ClientId + " connection closed");
            if (OnDeviceConnectionStateChanged != null)
            {
                OnDeviceConnectionStateChanged(macroDeckClient, EventArgs.Empty);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Closes the connection
        /// </summary>
        /// <param name="macroDeckClient"></param>
        public static void CloseClient(MacroDeckClient macroDeckClient)
        {
            if (macroDeckClient != null && macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
            {
                MacroDeckLogger.Info("Close connection to " + macroDeckClient.ClientId);
                macroDeckClient.SocketConnection.Close();
            }
        }
        

        private static void OnMessage(MacroDeckClient macroDeckClient, string jsonMessageString)
        {
            JObject responseObject = JObject.Parse(jsonMessageString);

            if (responseObject["Method"] == null) return;

            if (!Enum.TryParse(typeof(JsonMethod), responseObject["Method"].ToString(), out object method)) return;

            MacroDeckLogger.Trace("Received method: " + method.ToString());

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

                    DeviceType deviceType = DeviceType.Unknown;
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


                    if (OnDeviceConnectionStateChanged != null)
                    {
                        OnDeviceConnectionStateChanged(macroDeckClient, EventArgs.Empty);
                    }
                    MacroDeckLogger.Info(macroDeckClient.ClientId + " connected");
                    break;
                case JsonMethod.BUTTON_PRESS:
                    try
                    {
                        if (macroDeckClient == null ||macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
                        int row = Int32.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        int column = Int32.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        ActionButton.ActionButton actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            ExecutePress(actionButton, macroDeckClient.ClientId);
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Action button press caused an exception: " + ex.Message);
                    }
                    break;
                case JsonMethod.BUTTON_RELEASE:
                    try
                    {
                        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
                        int row = Int32.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        int column = Int32.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        ActionButton.ActionButton actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            ExecuteRelease(actionButton, macroDeckClient.ClientId);
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Action button release caused an exception: " + ex.Message);
                    }
                    break;
                case JsonMethod.BUTTON_LONG_PRESS:
                    try
                    {
                        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
                        int row = Int32.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        int column = Int32.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        ActionButton.ActionButton actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            ExecuteLongPress(actionButton, macroDeckClient.ClientId);
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Action button long press caused an exception: " + ex.Message);
                    }
                    break;
                case JsonMethod.BUTTON_LONG_PRESS_RELEASE:
                    try
                    {
                        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
                        int row = Int32.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        int column = Int32.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        ActionButton.ActionButton actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            ExecuteLongPressRelease(actionButton, macroDeckClient.ClientId);
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
                case JsonMethod.GET_ICONS:
                    Task.Run(() =>
                    {
                        //SendAllIcons(macroDeckClient);
                    });
                    break;
            }
        }

        public static void ExecutePress(ActionButton.ActionButton actionButton, string clientId)
        {
            Task.Run(() =>
            {
                foreach (PluginAction action in actionButton.Actions)
                {
                    try
                    {
                        action.Trigger(clientId, actionButton);
                    } catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }

        public static void ExecuteRelease(ActionButton.ActionButton actionButton, string clientId)
        {
            Task.Run(() =>
            {
                foreach (PluginAction action in actionButton.ActionsRelease)
                {
                    try
                    {
                        action.Trigger(clientId, actionButton);
                    }
                    catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }
        public static void ExecuteLongPress(ActionButton.ActionButton actionButton, string clientId)
        {
            Task.Run(() =>
            {
                foreach (PluginAction action in actionButton.ActionsLongPress)
                {
                    try
                    {
                        action.Trigger(clientId, actionButton);
                    }
                    catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }

        public static void ExecuteLongPressRelease(ActionButton.ActionButton actionButton, string clientId)
        {
            Task.Run(() =>
            {
                foreach (PluginAction action in actionButton.ActionsLongPressRelease)
                {
                    try
                    {
                        action.Trigger(clientId, actionButton);
                    }
                    catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }

        /// <summary>
        /// Sets the current profile of a client
        /// </summary>
        /// <param name="macroDeckClient"></param>
        /// <param name="macroDeckProfile"></param>
        public static void SetProfile(MacroDeckClient macroDeckClient, MacroDeckProfile macroDeckProfile)
        {
            if (macroDeckProfile == null) return;
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
            if (macroDeckClient == null) return;
            if (folder == null) return;
            macroDeckClient.Folder = folder;
            SendAllButtons(macroDeckClient);
            if (OnFolderChanged != null)
            {
                OnFolderChanged(macroDeckClient, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Updates the folder on all clients with this folder as the current folder
        /// </summary>
        /// <param name="folder"></param>
        public static void UpdateFolder(MacroDeckFolder folder)
        {
            foreach (MacroDeckClient macroDeckClient in _clients.FindAll(delegate (MacroDeckClient macroDeckClient)
            {
                return macroDeckClient.Folder.Equals(folder);
            }))
            {
                SendAllButtons(macroDeckClient);
            }
        }


        /// <summary>
        /// Sends all icon packs to the client
        /// </summary>
        /// <param name="macroDeckClient"></param>
        /*public static void SendAllIcons(MacroDeckClient macroDeckClient = null)
        {
            if (macroDeckClient == null)
            {
                foreach (MacroDeckClient client in MacroDeckServer.Clients)
                {
                    client.DeviceMessage.SendIconPacks(client);
                }
            } else
            {
                macroDeckClient.DeviceMessage.SendIconPacks(macroDeckClient);
            }
        }*/

        /// <summary>
        /// Sends all buttons of the current folder to the client
        /// </summary>
        /// <param name="macroDeckClient"></param>
        private static void SendAllButtons(MacroDeckClient macroDeckClient)
        {
            macroDeckClient.DeviceMessage.SendAllButtons(macroDeckClient);
        }

        /// <summary>
        /// Sends a single button to the client
        /// </summary>
        /// <param name="macroDeckClient"></param>
        /// <param name="actionButton"></param>
        public static void SendButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
        {
            macroDeckClient.DeviceMessage.UpdateButton(macroDeckClient, actionButton);
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
            foreach (MacroDeckClient macroDeckClient in _clients.FindAll(delegate (MacroDeckClient macroDeckClient)
            {
                return macroDeckClient.Folder.ActionButtons.Contains(actionButton);
            }))
            {
                SendButton(macroDeckClient, actionButton);
            }
        }

        /// <summary>
        /// Get the MacroDeckClient from the client id
        /// </summary>
        /// <param name="macroDeckClientId">Client-ID</param>
        /// <returns></returns>
        public static MacroDeckClient GetMacroDeckClient(string macroDeckClientId)
        {
            if (string.IsNullOrWhiteSpace(macroDeckClientId)) return null;
            return _clients.Find(macroDeckClient => macroDeckClient.ClientId == macroDeckClientId);
        }

        /// <summary>
        /// Raw send function
        /// </summary>
        /// <param name="socketConnection"></param>
        /// <param name="jObject"></param>
        internal static void Send(IWebSocketConnection socketConnection, JObject jObject)
        {
            socketConnection.Send(jObject.ToString());
        }
    }
}
