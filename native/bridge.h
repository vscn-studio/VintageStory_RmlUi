// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

#pragma once
#include <cstdint>
#if defined(_WIN32)
#define VR_API extern "C" __declspec(dllexport)
#define VR_CALL __cdecl
#else
#define VR_API extern "C" __attribute__((visibility("default")))
#define VR_CALL
#endif
// All calls and callbacks are synchronous and confined to the initialization thread.
// Read allocates host memory; Free releases it. Image data is premultiplied RGBA8.
using ReadCallback = int(VR_CALL*)(int kind, const char* path, void** data, int* length, int* width, int* height);
using FreeCallback = void(VR_CALL*)(void* data);
using LogCallback = void(VR_CALL*)(int level, const char* message);
using WriteCallback = void(VR_CALL*)(int kind, const char* value);
struct Callbacks { ReadCallback read; FreeCallback free; LogCallback log; WriteCallback write; };
struct NativeEvent { uint64_t subscription; uint64_t document; uint64_t target; };
VR_API int VR_CALL vr_abi();
VR_API const char* VR_CALL vr_error();
VR_API int VR_CALL vr_init(const Callbacks* callbacks, int headless);
VR_API int VR_CALL vr_shutdown();
VR_API uint64_t VR_CALL vr_load(const char* path, const char* markup, int width, int height, float scale);
VR_API int64_t VR_CALL vr_doc(uint64_t document, int operation, int a, int b, float value, const char* text);
VR_API uint64_t VR_CALL vr_element(uint64_t document, uint64_t element, int operation, const char* name, const char* value);
VR_API const char* VR_CALL vr_get(uint64_t document, uint64_t element, int operation, const char* name);
VR_API int VR_CALL vr_query_all(uint64_t document, uint64_t element, const char* selector, uint64_t* output, int capacity);
VR_API int VR_CALL vr_font(const char* path, const char* family, int weight, int italic, int fallback);
VR_API uint64_t VR_CALL vr_listen(uint64_t document, uint64_t element, const char* type, int capture);
VR_API int VR_CALL vr_unlisten(uint64_t subscription);
// Poll: 0 = empty, 1 = event, 2 = detached subscription (only subscription is set), -1 = error.
VR_API int VR_CALL vr_poll(NativeEvent* result);
VR_API const char* VR_CALL vr_event_value(int field);
