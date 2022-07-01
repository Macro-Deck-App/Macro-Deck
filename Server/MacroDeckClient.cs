using Fleck;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Server.DeviceMessage;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SuchByte.MacroDeck.Server
{
    public class MacroDeckClient : IDisposable
    {
        private IntPtr _bufferPtr;
        private int BUFFER_SIZE = 1024 * 1024; // 1 MB
        private bool _disposed = false;

        public MacroDeckClient()
        {
            _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Free any other managed objects here.
            }

            // Free any unmanaged objects here.
            Marshal.FreeHGlobal(_bufferPtr);
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MacroDeckClient()
        {
            Dispose(false);
        }

        private IWebSocketConnection _socketConnection;
        private string _clientId;

        private DeviceType _deviceType;

        public MacroDeckClient(IWebSocketConnection socket)
        {
            this._socketConnection = socket;
        }

        public void SetClientId(string clientId)
        {
            this._clientId = clientId;
        }

        public IWebSocketConnection SocketConnection { get { return this._socketConnection; } }

        public Folders.MacroDeckFolder Folder { get; set; }

        public Profiles.MacroDeckProfile Profile { get; set; }
        public string ClientId { get { return this._clientId; } }

        public DeviceClass DeviceClass { get; set; } = DeviceClass.SoftwareClient;


        public DeviceType DeviceType
        {
            get { return this._deviceType; }
            set
            {
                this._deviceType = value;
                switch (this._deviceType)
                {
                    case DeviceType.Unknown:
                    case DeviceType.Web:
                    case DeviceType.Android:
                    case DeviceType.StreamDeck:
                    case DeviceType.iOS:
                        this.DeviceClass = DeviceClass.SoftwareClient;
                        this.DeviceMessage = new SoftwareClientMessage();
                        break;
                }
            }
        }

        public IDeviceMessage DeviceMessage { get; set; }
    }
}
