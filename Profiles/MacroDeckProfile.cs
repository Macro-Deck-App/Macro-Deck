using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;

namespace SuchByte.MacroDeck.Profiles
{
    public class MacroDeckProfile : IDisposable
    {
        private readonly IntPtr _bufferPtr;
        public int BufferSize = 1024 * 1024; // 1 MB
        private bool _disposed;

        public MacroDeckProfile()
        {
            _bufferPtr = Marshal.AllocHGlobal(BufferSize);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                foreach (var folder in Folders)
                {
                    folder.Dispose();
                }
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

        ~MacroDeckProfile()
        {
            Dispose(false);
        }

        public string ProfileId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<MacroDeckFolder> Folders { get; set; } = new();
        public int Rows { get; set; } = 3;
        public int Columns { get; set; } = 5;
        public int ButtonSpacing { get; set; } = 10;
        public int ButtonRadius { get; set; } = 40;
        public bool ButtonBackground { get; set; } = true;
        public DeviceClass ProfileTarget { get; set; } = DeviceClass.SoftwareClient;

        public bool ButtonsCustomizable
        {
            get
            {
                switch (ProfileTarget)
                {
                    case DeviceClass.SoftwareClient:
                        return true;
                    case DeviceClass.Macro_Deck_DIY_OLED_6_V1:
                        return false;
                }
                return false;
            }
        }

    }
}
