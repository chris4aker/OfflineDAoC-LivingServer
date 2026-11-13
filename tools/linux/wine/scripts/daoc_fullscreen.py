#!/usr/bin/env python3
"""Keep the DAoC client window truly fullscreen under GNOME.

The launcher forces the client into borderless-window mode (fullscreen_windowed=1). Under
GNOME/XWayland that 1920x1080 window is not marked fullscreen, so Mutter pushes it below the
top bar and its bottom edge (Continue / Accept buttons) falls off the screen.

This watches for the client window (WM_CLASS "game.dll") and asks the window manager to make
it fullscreen, the same as `wmctrl -b add,fullscreen`.

    daoc_fullscreen.py            fix any client window now and exit
    daoc_fullscreen.py --watch    keep fixing new client windows until stopped
    daoc_fullscreen.py --watch --while-pid PID   ...and stop when PID exits
"""
import argparse
import ctypes
import ctypes.util
import os
import time

x = ctypes.cdll.LoadLibrary(ctypes.util.find_library("X11"))
ulong, vp = ctypes.c_ulong, ctypes.c_void_p
x.XOpenDisplay.restype = vp
x.XOpenDisplay.argtypes = [ctypes.c_char_p]
x.XInternAtom.restype = ulong
x.XInternAtom.argtypes = [vp, ctypes.c_char_p, ctypes.c_int]
x.XDefaultRootWindow.restype = ulong
x.XDefaultRootWindow.argtypes = [vp]
x.XQueryTree.argtypes = [vp, ulong, ctypes.POINTER(ulong), ctypes.POINTER(ulong),
                         ctypes.POINTER(ctypes.POINTER(ulong)), ctypes.POINTER(ctypes.c_uint)]
x.XFree.argtypes = [vp]
x.XGetWindowProperty.argtypes = [vp, ulong, ulong, ctypes.c_long, ctypes.c_long, ctypes.c_int, ulong,
                                 ctypes.POINTER(ulong), ctypes.POINTER(ctypes.c_int),
                                 ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_ulong),
                                 ctypes.POINTER(ctypes.POINTER(ctypes.c_ubyte))]
x.XSetErrorHandler.argtypes = [vp]


class XWindowAttributes(ctypes.Structure):
    _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int), ("width", ctypes.c_int), ("height", ctypes.c_int),
                ("border_width", ctypes.c_int), ("depth", ctypes.c_int), ("visual", vp), ("root", ulong),
                ("c_class", ctypes.c_int), ("bit_gravity", ctypes.c_int), ("win_gravity", ctypes.c_int),
                ("backing_store", ctypes.c_int), ("backing_planes", ulong), ("backing_pixel", ulong),
                ("save_under", ctypes.c_int), ("colormap", ulong), ("map_installed", ctypes.c_int),
                ("map_state", ctypes.c_int), ("all_event_masks", ctypes.c_long), ("your_event_mask", ctypes.c_long),
                ("do_not_propagate_mask", ctypes.c_long), ("override_redirect", ctypes.c_int), ("screen", vp)]


x.XGetWindowAttributes.argtypes = [vp, ulong, ctypes.POINTER(XWindowAttributes)]


class XClientMessageEvent(ctypes.Structure):
    _fields_ = [("type", ctypes.c_int), ("serial", ulong), ("send_event", ctypes.c_int), ("display", vp),
                ("window", ulong), ("message_type", ulong), ("format", ctypes.c_int), ("l", ctypes.c_long * 5)]


class XEvent(ctypes.Union):
    _fields_ = [("xclient", XClientMessageEvent), ("pad", ctypes.c_long * 24)]


x.XSendEvent.argtypes = [vp, ulong, ctypes.c_int, ctypes.c_long, ctypes.POINTER(XEvent)]

# Ignore BadWindow errors from windows that vanish while we look at them.
_ERR = ctypes.CFUNCTYPE(ctypes.c_int, vp, vp)(lambda d, e: 0)
x.XSetErrorHandler(_ERR)


def get_prop(d, w, atom, req_type):
    t, fmt = ulong(), ctypes.c_int()
    n, after = ctypes.c_ulong(), ctypes.c_ulong()
    data = ctypes.POINTER(ctypes.c_ubyte)()
    if x.XGetWindowProperty(d, w, atom, 0, 1024, 0, req_type, ctypes.byref(t), ctypes.byref(fmt),
                            ctypes.byref(n), ctypes.byref(after), ctypes.byref(data)) != 0 or not data:
        return None, 0
    size = {8: 1, 16: ctypes.sizeof(ctypes.c_short), 32: ctypes.sizeof(ctypes.c_long)}.get(fmt.value, 1)
    raw = ctypes.string_at(data, n.value * size)
    x.XFree(data)
    return raw, fmt.value


def children(d, w):
    root, parent = ulong(), ulong()
    kids = ctypes.POINTER(ulong)()
    count = ctypes.c_uint()
    if not x.XQueryTree(d, w, ctypes.byref(root), ctypes.byref(parent), ctypes.byref(kids), ctypes.byref(count)):
        return []
    out = [kids[i] for i in range(count.value)]
    if kids:
        x.XFree(kids)
    return out


def client_windows(d, root, atoms):
    found, stack = [], [root]
    while stack:
        w = stack.pop()
        raw, _ = get_prop(d, w, atoms["WM_CLASS"], 0)  # AnyPropertyType
        if raw and raw.split(b"\0")[0].lower() == b"game.dll":
            attrs = XWindowAttributes()
            if x.XGetWindowAttributes(d, w, ctypes.byref(attrs)) and attrs.width >= 640 and attrs.map_state == 2:
                found.append(w)
        stack.extend(children(d, w))
    return found


def is_fullscreen(d, w, atoms):
    raw, fmt = get_prop(d, w, atoms["_NET_WM_STATE"], 4)  # XA_ATOM
    if not raw or fmt != 32:
        return False
    vals = (ctypes.c_long * (len(raw) // ctypes.sizeof(ctypes.c_long))).from_buffer_copy(raw)
    return atoms["_NET_WM_STATE_FULLSCREEN"] in [v & 0xFFFFFFFF for v in vals]


def make_fullscreen(d, root, w, atoms):
    e = XEvent()
    c = e.xclient
    c.type, c.send_event, c.window, c.format = 33, 1, w, 32  # ClientMessage
    c.message_type = atoms["_NET_WM_STATE"]
    c.l[0], c.l[1], c.l[3] = 1, atoms["_NET_WM_STATE_FULLSCREEN"], 1  # _NET_WM_STATE_ADD, source=application
    e.xclient = c
    x.XSendEvent(d, root, 0, (1 << 20) | (1 << 19), ctypes.byref(e))  # SubstructureRedirect|Notify
    x.XFlush(d)


def fix_once(d, root, atoms, verbose):
    fixed = 0
    for w in client_windows(d, root, atoms):
        if not is_fullscreen(d, w, atoms):
            make_fullscreen(d, root, w, atoms)
            fixed += 1
            if verbose:
                print(f"daoc_fullscreen: made window 0x{w:x} fullscreen", flush=True)
    return fixed


def alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--watch", action="store_true", help="keep watching for new client windows")
    ap.add_argument("--while-pid", type=int, help="with --watch, stop when this process exits")
    ap.add_argument("--interval", type=float, default=2.0)
    args = ap.parse_args()

    d = x.XOpenDisplay(None)
    if not d:
        raise SystemExit("daoc_fullscreen: cannot open the X display (is DISPLAY set?)")
    root = x.XDefaultRootWindow(d)
    atoms = {n: x.XInternAtom(d, n.encode(), 0) for n in ("WM_CLASS", "_NET_WM_STATE", "_NET_WM_STATE_FULLSCREEN")}

    if not args.watch:
        n = fix_once(d, root, atoms, True)
        if n == 0:
            print("daoc_fullscreen: no client window needed fixing")
        return
    while args.while_pid is None or alive(args.while_pid):
        fix_once(d, root, atoms, True)
        time.sleep(args.interval)


if __name__ == "__main__":
    main()
