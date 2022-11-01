﻿using System;
using System.Runtime.InteropServices;
using Fleck;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server.DeviceMessage;

namespace SuchByte.MacroDeck.Server;

public class MacroDeckClient : IDisposable
{
    private IntPtr _bufferPtr;
    private int BUFFER_SIZE = 1024 * 1024; // 1 MB
    private bool _disposed;

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
        _socketConnection = socket;
    }

    public void SetClientId(string clientId)
    {
        _clientId = clientId;
    }

    public IWebSocketConnection SocketConnection => _socketConnection;

    public MacroDeckFolder Folder { get; set; }

    public MacroDeckProfile Profile { get; set; }
    public string ClientId => _clientId;

    public DeviceClass DeviceClass { get; set; } = DeviceClass.SoftwareClient;


    public DeviceType DeviceType
    {
        get => _deviceType;
        set
        {
            _deviceType = value;
            switch (_deviceType)
            {
                case DeviceType.Unknown:
                case DeviceType.Web:
                case DeviceType.Android:
                case DeviceType.StreamDeck:
                case DeviceType.iOS:
                    DeviceClass = DeviceClass.SoftwareClient;
                    DeviceMessage = new SoftwareClientMessage();
                    break;
            }
        }
    }

    public IDeviceMessage DeviceMessage { get; set; }
}