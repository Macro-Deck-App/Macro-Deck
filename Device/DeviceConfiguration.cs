using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Device
{
    public class DeviceConfiguration
    {
        public float Brightness = 0.3f;
        public bool AutoConnect = false;
        public WakeLockMethod WakeLockMethod = WakeLockMethod.Connected;
    }

    public enum WakeLockMethod
    {
        Always,
        Connected,
        Never
    }
}
