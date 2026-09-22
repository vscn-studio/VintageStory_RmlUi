#!/usr/bin/env python3
# Copyright (c) 2026 VSCN-Studio
# SPDX-License-Identifier: MIT

"""Game-independent C ABI smoke check, including 64-bit handles and UTF-8 callbacks."""
import ctypes as c
from pathlib import Path
import sys

lib = c.CDLL(str(Path(sys.argv[1]).resolve()))
Read = c.CFUNCTYPE(c.c_int, c.c_int, c.c_char_p, c.POINTER(c.c_void_p), c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_int))
Free = c.CFUNCTYPE(None, c.c_void_p)
Log = c.CFUNCTYPE(None, c.c_int, c.c_char_p)
Write = c.CFUNCTYPE(None, c.c_int, c.c_char_p)


class Callbacks(c.Structure):
    _fields_ = [("read", Read), ("free", Free), ("log", Log), ("write", Write)]


class Event(c.Structure):
    _fields_ = [("subscription", c.c_uint64), ("document", c.c_uint64), ("target", c.c_uint64)]


def bind(name, result, *arguments):
    function = getattr(lib, name)
    function.restype, function.argtypes = result, arguments
    return function


abi = bind("vr_abi", c.c_int)
error = bind("vr_error", c.c_char_p)
init = bind("vr_init", c.c_int, c.POINTER(Callbacks), c.c_int)
shutdown = bind("vr_shutdown", c.c_int)
load = bind("vr_load", c.c_uint64, c.c_char_p, c.c_char_p, c.c_int, c.c_int, c.c_float)
doc = bind("vr_doc", c.c_int64, c.c_uint64, c.c_int, c.c_int, c.c_int, c.c_float, c.c_char_p)
element = bind("vr_element", c.c_uint64, c.c_uint64, c.c_uint64, c.c_int, c.c_char_p, c.c_char_p)
get = bind("vr_get", c.c_char_p, c.c_uint64, c.c_uint64, c.c_int, c.c_char_p)
listen = bind("vr_listen", c.c_uint64, c.c_uint64, c.c_uint64, c.c_char_p, c.c_int)
poll = bind("vr_poll", c.c_int, c.POINTER(Event))
event_value = bind("vr_event_value", c.c_char_p, c.c_int)
font_face = bind("vr_font_face", c.c_int, c.c_char_p, c.c_char_p, c.c_int, c.c_int, c.c_int, c.c_int)
buffers = {}
messages = []
markup = '<rml><body><input id="entry" type="text" value="中文"/><button id="button"/></body></rml>'.encode()


@Read
def read(kind, path, data, length, width, height):
    if kind != 0 or path != b"smoke:dialog/test.rml":
        return 0
    buffer = c.create_string_buffer(markup)
    address = c.addressof(buffer)
    buffers[address] = buffer
    data[0], length[0], width[0], height[0] = address, len(markup), 0, 0
    return 1


@Free
def free(address):
    buffers.pop(address, None)


callbacks = Callbacks(read, free, Log(lambda level, message: messages.append((level, message.decode()))), Write(lambda kind, value: None))


def check(condition, message):
    if not condition:
        raise RuntimeError(f"{message}: {error()!r}; logs={messages}")


check(abi() == 2, "ABI version")
for cycle in range(3):
    check(init(c.byref(callbacks), 1) == 1, "headless initialization")
    check(font_face(b"smoke:fonts/missing.ttc", b"test", 400, 0, 0, -1) == 0
          and b"face index" in error(), "invalid collection index rejected")
    document = load(b"smoke:dialog/test.rml", None, 800, 600, 1.0)
    check(document != 0, "file callback/document load")
    entry = element(document, 0, 0, b"entry", None)
    check(entry != 0 and get(document, entry, 3, None).decode() == "中文", "UTF-8 initial value")
    value = "跨平台🙂".encode()
    check(element(document, entry, 9, None, value) != 0, "set UTF-8 value")
    check(get(document, entry, 3, None) == value, "UTF-8 round trip")
    token = listen(document, entry, b"change", 0)
    check(token != 0, "event subscription")
    check(element(document, entry, 14, b"change", value) != 0, "event dispatch")
    event = Event()
    check(poll(c.byref(event)) == 1 and event.subscription == token and event.document == document, "event ABI/handle identity")
    check(event_value(1) == value, "event UTF-8 payload")
    check(doc(document, 0, 0, 0, 0, None) == 1, "document release")
    check(get(document, entry, 3, None) is None and bool(error()), "stale handle rejected")
    check(shutdown() == 1, "shutdown")
    check(not buffers, "host buffers released")
print("PASS: native C ABI, UTF-8 file/event callbacks, stale handles, three lifecycle cycles")
