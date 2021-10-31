using SuchByte.MacroDeck.Device;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SuchByte.MacroDeck.Folders
{
    public class MacroDeckFolder : IDisposable
    {
        private IntPtr _bufferPtr;
        public int BUFFER_SIZE = 1024 * 1024; // 1 MB
        private bool _disposed = false;

        public MacroDeckFolder()
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

        ~MacroDeckFolder()
        {
            Dispose(false);
        }

        public long FolderId { get; set; }
        public String DisplayName { get; set; }
        public List<long> Childs { get; set; } = new List<long>();
        public List<ActionButton.ActionButton> ActionButtons { get; set; }
        public List<string> ApplicationsFocusDevices { get; set; } = new List<string>();
        public string ApplicationToTrigger { get; set; } = "";
    }
}
