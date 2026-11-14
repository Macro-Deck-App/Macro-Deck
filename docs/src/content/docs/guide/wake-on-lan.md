---
title: Wake on LAN
description: How Macro Deck turns on a PC, NAS or other device on your network with a Wake on LAN magic packet, what each device setting means, and what to check when a device does not wake up.
---

Wake on LAN turns on a device that is switched off or asleep by sending it a small network packet, the magic
packet. The Wake on LAN integration saves the devices you want to wake and gives you a **Wake device** action
for a button, automation or script. It works on Windows, macOS and Linux and needs no account or online
service: the packet stays on your network.

## Preparing the device

The device you want to wake has to allow it. Wake on LAN is usually turned off by default:

- In the device's firmware (BIOS or UEFI), turn on the option called Wake on LAN, Power On by PCI-E or similar.
- In the operating system, allow the network adapter to wake the computer. On Windows this is in the
  adapter's properties in Device Manager, under **Power Management** and **Advanced**.
- Use a wired connection. Most Wi-Fi adapters cannot be woken this way.
- A NAS or another device usually has a Wake on LAN setting in its own settings.

You also need the MAC address of the device's network adapter. It is shown in the network settings of the
device or of your router, as six pairs of hexadecimal digits such as `AA:BB:CC:DD:EE:FF`.

## Saving a device

Open **Integrations**, choose **Wake on LAN** and add a configuration for each device:

| Setting | What it does |
| --- | --- |
| **Configuration name** | The name you pick the device by in the action, for example *Gaming PC*. |
| **MAC address** | The device's MAC address. `AA:BB:CC:DD:EE:FF`, `AA-BB-CC-DD-EE-FF`, `AABB.CCDD.EEFF` and `AABBCCDDEEFF` are all accepted; it is saved as `AA:BB:CC:DD:EE:FF`. |
| **Broadcast address** | Where the packet is sent. Keep `255.255.255.255` to reach every device on the network this computer is in. For a device in another subnet, enter that subnet's broadcast address, for example `192.168.2.255`. Only IPv4 addresses are accepted. |
| **Port** | The UDP port the packet is sent to. Most devices listen on port 9, some on 7. |
| **Packets** | How many packets to send, from 1 to 10. Send more than one to a device that does not always wake up. |
| **Delay between packets (ms)** | How long to wait between two packets, from 0 to 10000 milliseconds. It only matters when you send more than one packet. |

A broadcast to another subnet only arrives when the router between the two networks forwards it, which
most home routers do not do.

## Waking a device

Add the action **Wake device** to a button, automation or script and choose the device. When the action runs,
Macro Deck sends the packets and reports success once they were sent.

A sent packet does not confirm that the device woke up: the device does not answer it. Whether the device is
online has to be checked separately.

## When a device does not wake up

- **Check the device first.** Wake on LAN has to be allowed in the firmware and for the network adapter, and the
  device has to be connected by cable. After a power cut, many computers only wake up again once they were
  turned on by hand.
- **Check the MAC address.** It has to belong to the adapter that is connected to the network, not to the
  Wi-Fi adapter of the same computer.
- **A VPN or several network adapters.** `255.255.255.255` goes out through one network only, and with a VPN or
  more than one network adapter that may be the wrong one. Enter the broadcast address of the device's subnet
  instead, for example `192.168.1.255` when the device has an address starting with `192.168.1.`.
- **Local Network access on macOS.** macOS can block Macro Deck from reaching devices on your local network.
  The action then fails with a message that the packet was not allowed or that there is no route to the
  address. Allow Macro Deck under **System Settings**, **Privacy & Security**, **Local Network**.
- **Send more packets.** Some devices miss a single packet while they are in a deep sleep. Set **Packets** to
  3 and the delay to 500 milliseconds.
