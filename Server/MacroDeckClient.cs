using Fleck;
using SuchByte.MacroDeck.Device;
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

        public DeviceType DeviceType { get; set; }
    }
}
