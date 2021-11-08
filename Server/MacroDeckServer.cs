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

namespace SuchByte.MacroDeck.Server
{
    public static class MacroDeckServer
    {
        public static event EventHandler OnDeviceConnectionStateChanged;
        public static event EventHandler OnServerStateChanged;
        public static event EventHandler OnIconChanged;
        public static event EventHandler OnLabelChanged;
        public static event EventHandler OnFolderChanged;

        private static WebSocketServer _webSocketServer;

        public static WebSocketServer WebSocketServer { get { return _webSocketServer; } }


        private static readonly List<MacroDeckClient> _clients = new List<MacroDeckClient>();
        public static List<MacroDeckClient> Clients { get { return _clients; } }


        public static void Start(string ipAddress, int port)
        {
            DeviceManager.LoadKnownDevices();
            Thread.Sleep(100);
            if (_webSocketServer != null)
            {
                Debug.WriteLine("Stopping websocket server...");
                foreach (MacroDeckClient macroDeckClient in _clients)
                {
                    if (macroDeckClient.SocketConnection != null && macroDeckClient.SocketConnection.IsAvailable)
                    {
                        macroDeckClient.SocketConnection.Close();
                    }
                }
                _webSocketServer.Dispose();
                _clients.Clear();
                if (OnServerStateChanged != null)
                {
                    OnServerStateChanged(_webSocketServer, EventArgs.Empty);
                }
                
            }
            Debug.WriteLine(String.Format("Starting websocket server @ {0}:{1}", ipAddress, port));
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
                Debug.WriteLine(ex.Message);
                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.FailedToStartServer, MessageBoxButtons.OK);
                }
            }
        }

        private static void OnOpen(MacroDeckClient macroDeckClient)
        {
            if (MacroDeck.Configuration.BlockNewConnections)
            {
                macroDeckClient.SocketConnection.Close();
                return;
            }
            if (Clients.Count >= 5)
            {
                macroDeckClient.SocketConnection.Close();
               /* using (GUI.CustomControls.MessageBox messageBox = new GUI.CustomControls.MessageBox())
                {
                    messageBox.ShowDialog("Connection limit reached", "With Macro Deck free you can connect up to 2 devices at the same time. If you want to connect more devices at the same time, upgrade to Macro Deck pro.", MessageBoxButtons.OK);
                    messageBox.Dispose();
                }*/
                return;
            } 
            if (ProfileManager.CurrentProfile.Folders == null || ProfileManager.CurrentProfile.Folders.Count < 1)
            {
                macroDeckClient.SocketConnection.Close();
                return;
            }

            _clients.Add(macroDeckClient);
            if (OnDeviceConnectionStateChanged != null)
            {
                OnDeviceConnectionStateChanged(macroDeckClient, EventArgs.Empty);
            }
        }

        private static void OnClose(MacroDeckClient macroDeckClient)
        {
            macroDeckClient.Dispose();
            _clients.Remove(macroDeckClient);
            if (OnDeviceConnectionStateChanged != null)
            {
                OnDeviceConnectionStateChanged(macroDeckClient, EventArgs.Empty);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public static void SetProfile(MacroDeckClient macroDeckClient, MacroDeckProfile macroDeckProfile)
        {
            if (macroDeckProfile == null) return;
            macroDeckClient.Profile = macroDeckProfile;
            SendConfiguration(macroDeckClient);
            SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
        }

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

        public static void SendConfiguration(MacroDeckClient macroDeckClient)
        {
            if (macroDeckClient == null || macroDeckClient.SocketConnection == null || !macroDeckClient.SocketConnection.IsAvailable) return;
            JObject configurationObject = JObject.FromObject(new
            {
                Method = JsonMethod.GET_CONFIG.ToString(),
                Rows = macroDeckClient.Profile.Rows,
                Columns = macroDeckClient.Profile.Columns,
                ButtonSpacing = macroDeckClient.Profile.ButtonSpacing,
                ButtonRadius = macroDeckClient.Profile.ButtonRadius,
                ButtonBackground = macroDeckClient.Profile.ButtonBackground,
                Brightness = DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.Brightness,
            });
            Send(macroDeckClient.SocketConnection, configurationObject);
            GC.Collect();
        }

        private static void OnMessage(MacroDeckClient macroDeckClient, string jsonMessageString)
        {
            JObject responseObject = JObject.Parse(jsonMessageString);

            Debug.WriteLine("in: " + jsonMessageString);

            switch (Enum.Parse(typeof(JsonMethod), responseObject["Method"].ToString()))
            {
                case JsonMethod.CONNECTED:
                    if (Int32.Parse(responseObject["API"].ToString()) != MacroDeck.ApiVersion)
                    {
                        macroDeckClient.SocketConnection.Close();
                        return;
                    }

                    macroDeckClient.SetClientId(responseObject["Client-Id"].ToString());

                    if (responseObject["Device-Type"] != null)
                    {
                        try
                        {
                            DeviceType deviceType = DeviceType.Unknown;
                            Enum.TryParse(responseObject["Device-Type"].ToString(), out deviceType);
                            macroDeckClient.DeviceType = deviceType;
                        }
                        catch { }
                    }

                    
                    if (MacroDeck.Configuration.AskOnNewConnections)
                    {
                        if (DeviceManager.IsKnownDevice(macroDeckClient.ClientId))
                        {
                            MacroDeckDevice macroDeckDevice = DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId);
                            if (macroDeckDevice.Blocked)
                            {
                                macroDeckClient.SocketConnection.Close();
                                return;
                            }
                            else
                            {
                                //macroDeckDevice.Available = true;
                                macroDeckDevice.ClientId = macroDeckClient.ClientId;
                                if (macroDeckDevice.ProfileId == "")
                                {
                                    macroDeckDevice.ProfileId = ProfileManager.Profiles[0].ProfileId;
                                }
                            }
                        }
                        else
                        {
                            Form mainForm = null;
                            foreach (Form form in Application.OpenForms)
                            {
                                if (form.Name.Equals("MainWindow"))
                                {
                                    mainForm = form;
                                }
                            }
                            if (mainForm != null)
                            {
                                Debug.WriteLine("Invoking");
                                mainForm.Invoke(new Action(() =>
                                {
                                    using (var msgBox = new GUI.CustomControls.MessageBox())
                                    {
                                        System.Media.SystemSounds.Exclamation.Play();
                                        if (msgBox.ShowDialog(Language.LanguageManager.Strings.NewConnection, String.Format(Language.LanguageManager.Strings.XIsAnUnknownDevice, macroDeckClient.ClientId), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                        {
                                            MacroDeckDevice macroDeckDevice = new MacroDeckDevice
                                            {
                                                ClientId = macroDeckClient.ClientId,
                                                DisplayName = "Client " + macroDeckClient.ClientId,
                                                ProfileId = ProfileManager.Profiles[0].ProfileId
                                            };
                                            DeviceManager.AddKnownDevice(macroDeckDevice);
                                        }
                                        else
                                        {
                                            macroDeckClient.SocketConnection.Close();
                                            if (msgBox.ShowDialog(Language.LanguageManager.Strings.BlockConnection, String.Format(Language.LanguageManager.Strings.ShouldMacroDeckBlockConnectionsFromX, macroDeckClient.ClientId), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                            {
                                                MacroDeckDevice macroDeckDevice = new MacroDeckDevice
                                                {
                                                    ClientId = macroDeckClient.ClientId,
                                                    DisplayName = "Client " + macroDeckClient.ClientId,
                                                    Blocked = true
                                                };
                                                DeviceManager.AddKnownDevice(macroDeckDevice);
                                            }
                                            return;
                                        }
                                    }
                                }));
                            } else
                            {
                                using (var msgBox = new GUI.CustomControls.MessageBox())
                                {
                                    System.Media.SystemSounds.Exclamation.Play();
                                    msgBox.TopMost = true;
                                    msgBox.Focus();
                                    msgBox.BringToFront();
                                    msgBox.TopMost = false;
                                    msgBox.ShowInTaskbar = true;
                                    if (msgBox.ShowDialog(Language.LanguageManager.Strings.NewConnection, String.Format(Language.LanguageManager.Strings.XIsAnUnknownDevice, macroDeckClient.ClientId), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                    {
                                        MacroDeckDevice macroDeckDevice = new MacroDeckDevice
                                        {
                                            ClientId = macroDeckClient.ClientId,
                                            DisplayName = "Client " + macroDeckClient.ClientId,
                                            ProfileId = ProfileManager.Profiles[0].ProfileId
                                        };
                                        DeviceManager.AddKnownDevice(macroDeckDevice);
                                    }
                                    else
                                    {
                                        macroDeckClient.SocketConnection.Close();
                                        if (msgBox.ShowDialog(Language.LanguageManager.Strings.BlockConnection, String.Format(Language.LanguageManager.Strings.ShouldMacroDeckBlockConnectionsFromX, macroDeckClient.ClientId), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                        {
                                            MacroDeckDevice macroDeckDevice = new MacroDeckDevice
                                            {
                                                ClientId = macroDeckClient.ClientId,
                                                DisplayName = "Client " + macroDeckClient.ClientId,
                                                Blocked = true
                                            };
                                            DeviceManager.AddKnownDevice(macroDeckDevice);
                                        }
                                        return;
                                    }
                                }
                            }
                            
                        }
                    } else
                    {
                        if (!DeviceManager.IsKnownDevice(macroDeckClient.ClientId))
                        {
                            MacroDeckDevice macroDeckDevice = new MacroDeckDevice
                            {
                                ClientId = macroDeckClient.ClientId,
                                DisplayName = "Client " + macroDeckClient.ClientId,
                                //Available = true,
                                ProfileId = ProfileManager.Profiles[0].ProfileId
                            };
                            DeviceManager.AddKnownDevice(macroDeckDevice);
                        }
                    }

                    if (!macroDeckClient.SocketConnection.IsAvailable || DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId) == null)
                    {
                        return;
                    }

                    if (DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId == "")
                    {
                        DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId = ProfileManager.Profiles[0].ProfileId;
                    }

                    DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).DeviceType = macroDeckClient.DeviceType;

                    DeviceManager.SaveKnownDevices();

                    macroDeckClient.Profile = ProfileManager.FindProfileById(DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId);
                    if (macroDeckClient.Profile == null)
                    {
                        macroDeckClient.Profile = ProfileManager.Profiles[0];
                    }
                    macroDeckClient.Folder = macroDeckClient.Profile.Folders[0];

                    SendConfiguration(macroDeckClient);
                    SendAllIcons(macroDeckClient);
                    break;
                case JsonMethod.BUTTON_PRESS:
                    try
                    {
                        int row = Int32.Parse(responseObject["Message"].ToString().Split('_')[0]);
                        int column = Int32.Parse(responseObject["Message"].ToString().Split('_')[1]);

                        ActionButton.ActionButton actionButton = macroDeckClient.Folder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
                        if (actionButton != null)
                        {
                            Trigger(actionButton, macroDeckClient.ClientId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
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
                        SendAllIcons(macroDeckClient);
                    });
                    break;
                default:
                    Console.WriteLine("Unknown method");
                    break;
            }
        }

        public static void Trigger(ActionButton.ActionButton actionButton, string clientId)
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
                //ButtonDone(clientId, row, column);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }

        private static void ButtonDone(MacroDeckClient macroDeckClient, int row, int column)
        {
            JObject actionButtonObject = JObject.FromObject(new
            {
                Position_Y = row,
                Position_X = column,
            });
            JObject updateObject = JObject.FromObject(new
            {
                Method = JsonMethod.BUTTON_DONE.ToString(),
                Buttons = new List<JObject>
                {
                    actionButtonObject
                },
            });
            Send(macroDeckClient.SocketConnection, updateObject);
        }

        /// <summary>
        /// Send icon base 64 to an action button
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="actionButton"></param>
        /// <param name="base64"></param>
        public static void SendIconBase64(string clientId, ActionButton.ActionButton actionButton, string base64)
        {
            SendIconBase64(GetMacroDeckClient(clientId), actionButton, base64);
        }

        public static void SendIconBase64(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton, string base64)
        {
            JObject actionButtonObject = JObject.FromObject(new
            {
                Position_X = actionButton.Position_X,
                Position_Y = actionButton.Position_Y,
                Icon = base64
            });

            JObject updateObject = JObject.FromObject(new
            {
                Method = JsonMethod.ICON_BASE64.ToString(),
                Buttons = new List<JObject>
                {
                    actionButtonObject
                },
            });

            Send(macroDeckClient.SocketConnection, updateObject);

            if (OnIconChanged != null)
            {
                OnIconChanged(actionButton, EventArgs.Empty);
            }

        }

        /// <summary>
        /// Display text on an action button
        /// </summary>
        /// <param name="clientId">Client-ID</param>
        /// <param name="actionButton">Actionbutton</param>
        /// <param name="text">Text</param>
        /// <param name="size">Text-Size</param>
        public static void DisplayText(string clientId, ActionButton.ActionButton actionButton, string text, float size, bool overrideLabel = false)
        {
            DisplayText(GetMacroDeckClient(clientId), actionButton, text, size);
        }

        public static void DisplayText(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton, string text, float textSize)
        {
            Bitmap labelBmp = new Bitmap(250, 250);
            labelBmp = Utils.LabelGenerator.GetLabel(labelBmp, text, ButtonLabelPosition.CENTER, new Font("Impact", textSize), Color.White, Color.Black, new SizeF(2.0f, 2.0f));
            string labelBase64 = Utils.Base64.GetBase64FromBitmap(labelBmp);
            labelBmp.Dispose();

            ButtonLabel label = new ButtonLabel
            {
                LabelBase64 = labelBase64
            };

            JObject actionButtonObject = JObject.FromObject(new
            {
                Position_X = actionButton.Position_X,
                Position_Y = actionButton.Position_Y,
                Label = label
            });

            JObject updateObject = JObject.FromObject(new
            {
                Method = JsonMethod.UPDATE_LABEL.ToString(),
                Buttons = new List<JObject>
                {
                    actionButtonObject
                },
            });

            Send(macroDeckClient.SocketConnection, updateObject);

            if (OnLabelChanged != null)
            {
                OnLabelChanged(actionButton, EventArgs.Empty);
            }
        }

        private static void OverrideLabelText(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton, string text)
        {
            Task.Run(() => {
                ButtonLabel buttonLabel = actionButton.LabelOff;
                buttonLabel.LabelText = text;
                buttonLabel.LabelBase64 = Utils.Base64.GetBase64FromBitmap(Utils.LabelGenerator.GetLabel(new Bitmap(250, 250), text, ButtonLabelPosition.CENTER, new Font(buttonLabel.FontFamily, buttonLabel.Size), buttonLabel.LabelColor, Color.Black, new SizeF(2.0f, 2.0f)));
                actionButton.LabelOn = actionButton.LabelOff;
                SendButton(macroDeckClient, actionButton);
                if (OnLabelChanged != null)
                {
                    OnLabelChanged(actionButton, EventArgs.Empty);
                }
            });
        }



        public static void SendAllIcons(MacroDeckClient macroDeckClient = null)
        {
            JObject iconsObject = JObject.FromObject(new
            {
                Method = JsonMethod.GET_ICONS.ToString(),
                IconPacks = IconManager.IconPacks
            });

            if (macroDeckClient == null)
            {
                foreach (MacroDeckClient mdc in _clients)
                {
                    Send(mdc.SocketConnection, iconsObject);
                }
            } else {
                Send(macroDeckClient.SocketConnection, iconsObject);
            }
        }

        private static void SendAllButtons(MacroDeckClient macroDeckClient)
        {
            if (macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;

            List<JObject> buttons = new List<JObject>();
            foreach (ActionButton.ActionButton actionButton in macroDeckClient.Folder.ActionButtons)
            {
                string Icon = "";
                ButtonLabel Label = null;
                switch (actionButton.State)
                {
                    case false:
                        if (actionButton.IconOff != null)
                        {
                            if (actionButton.IconOff.Length > 0)
                            {
                                Icon = actionButton.IconOff;
                            }
                        }
                        Label = actionButton.LabelOff;
                        break;
                    case true:
                        if (actionButton.IconOn != null)
                        {
                            if (actionButton.IconOn.Length > 0)
                            {
                                Icon = actionButton.IconOn;
                            }
                        }
                        Label = actionButton.LabelOn;
                        break;
                }

                JObject actionButtonObject = JObject.FromObject(new
                {
                    actionButton.ButtonId,
                    Icon,
                    actionButton.Position_X,
                    actionButton.Position_Y,
                    Label,
                });
                buttons.Add(actionButtonObject);
            }

            JObject buttonsObject = JObject.FromObject(new
            {
                Method = JsonMethod.GET_BUTTONS.ToString(),
                Buttons = buttons
            });

            Send(macroDeckClient.SocketConnection, buttonsObject);
        }

        public static void SendButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
        {
            if (macroDeckClient.Folder == null) return;
            if (!macroDeckClient.Folder.ActionButtons.Contains(actionButton)) return;
            string Icon = "";
            ButtonLabel Label;
            if (actionButton.State == false)
            {
                if (actionButton.IconOff.Length > 0)
                {
                    if (actionButton.IconOff.Length > 0)
                    {
                        Icon = actionButton.IconOff;
                    }
                }
                Label = actionButton.LabelOff;
            }
            else
            {
                if (actionButton.IconOn.Length > 0)
                {
                    if (actionButton.IconOn.Length > 0)
                    {
                        Icon = actionButton.IconOn;
                    }
                }
                Label = actionButton.LabelOn;
            }
            JObject actionButtonObject = JObject.FromObject(new
            {
                actionButton.ButtonId,
                Icon,
                actionButton.Position_X,
                actionButton.Position_Y,
                Label,
            });

            JObject updateObject = JObject.FromObject(new
            {
                Method = JsonMethod.UPDATE_BUTTON.ToString(),
                Buttons = new List<JObject>
                {
                    actionButtonObject
                },
            });

            Send(macroDeckClient.SocketConnection, updateObject);
        }

        /// <summary>
        /// Set button state on or off
        /// </summary>
        /// <param name="actionButton">ActionButton</param>
        /// <param name="state">State true = on, off = false</param>
        public static void SetState(ActionButton.ActionButton actionButton, bool state)
        {
            actionButton.State = state;
            UpdateState(actionButton);
        }

        public static void UpdateState(ActionButton.ActionButton actionButton)
        {
            foreach (MacroDeckClient macroDeckClient in _clients.FindAll(delegate (MacroDeckClient macroDeckClient)
            {
                return macroDeckClient.Folder.ActionButtons.Contains(actionButton);
            }))
            {
                SendButton(macroDeckClient, actionButton);
            }
        }


        private static void Send(IWebSocketConnection socketConnection, JObject jObject)
        {
            socketConnection.Send(jObject.ToString());
        }


        /// <summary>
        /// Get the MacroDeckClient from the client id
        /// </summary>
        /// <param name="macroDeckClientId">Client-ID</param>
        /// <returns></returns>
        public static MacroDeckClient GetMacroDeckClient(string macroDeckClientId)
        {
            if (macroDeckClientId.Equals("")) return null;
            return _clients.Find(macroDeckClient => macroDeckClient.ClientId == macroDeckClientId);
        }

    }
}
