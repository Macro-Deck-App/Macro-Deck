import ctypes
import ctypes.util
import sys

x11 = ctypes.CDLL(ctypes.util.find_library("X11") or "libX11.so.6")
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XStringToKeysym.restype = ctypes.c_ulong
x11.XStringToKeysym.argtypes = [ctypes.c_char_p]
x11.XKeysymToKeycode.restype = ctypes.c_ubyte
x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x11.XQueryKeymap.argtypes = [ctypes.c_void_p, ctypes.c_char * 32]
x11.XFlush.argtypes = [ctypes.c_void_p]
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]


def main():
    command, keysym = sys.argv[1], sys.argv[2]
    display = x11.XOpenDisplay(None)
    if not display:
        print("cannot open the X display", file=sys.stderr)
        return 2
    keycode = x11.XKeysymToKeycode(display, x11.XStringToKeysym(keysym.encode()))
    try:
        if command == "held":
            keys = (ctypes.c_char * 32)()
            x11.XQueryKeymap(display, keys)
            return 0 if keys.raw[keycode // 8] & (1 << (keycode % 8)) else 1
        if command == "release":
            xtst = ctypes.CDLL(ctypes.util.find_library("Xtst") or "libXtst.so.6")
            xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
            xtst.XTestFakeKeyEvent(display, keycode, 0, 0)
            x11.XFlush(display)
            return 0
        print(f"unknown command {command}", file=sys.stderr)
        return 2
    finally:
        x11.XCloseDisplay(display)


sys.exit(main())
