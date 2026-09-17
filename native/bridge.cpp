// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

#include "bridge.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/Elements/ElementFormControl.h>
#include <RmlUi/Core/Elements/ElementFormControlSelect.h>
#include "RmlUi_Include_GL3.h"
#include "RmlUi_Renderer_GL3.h"
#include <algorithm>
#include <chrono>
#include <cstring>
#include <deque>
#include <memory>
#include <stdexcept>
#include <thread>
#include <unordered_map>
#include <vector>

using namespace Rml;
namespace {
Callbacks host{};
std::string error, result_string;
bool initialized = false, headless_mode = false;
std::thread::id owner_thread;
uint64_t next_id = 1; // Never reuse handles, including across shutdown/reinitialization.
GLuint target_framebuffer = 0;
String asset_url(const String& path) {
    // RmlUi intentionally uses '|' as an escaped colon in file paths. Unlike
    // a URI authority, this survives URL::GetPath() inside ElementImage.
    auto result = path; auto colon = result.find(':');
    if (colon != String::npos) result[colon] = '|';
    return result;
}
String asset_path(const String& url) {
    auto result = url; auto pipe = result.find('|');
    if (pipe != String::npos) result[pipe] = ':';
    return result;
}
void check_thread() {
    if (!initialized) throw std::runtime_error("RmlUi is not initialized.");
    if (owner_thread != std::this_thread::get_id()) throw std::runtime_error("RmlUi must be called on the client thread.");
}
template<class F, class T> T guard(F&& f, T failure) noexcept {
    error.clear();
    try { return f(); }
    catch (const std::exception& e) { error = e.what(); }
    catch (...) { error = "Unknown native exception."; }
    return failure;
}
struct HostBuffer {
    void* data = nullptr; int length = 0, width = 0, height = 0;
    HostBuffer(int kind, const char* path) { auto address = kind <= 1 ? asset_path(path) : String(path); if (!host.read(kind, address.c_str(), &data, &length, &width, &height)) { if (data) host.free(data); data = nullptr; } }
    ~HostBuffer() { if (data) host.free(data); }
    std::string text() const { return data && length > 0 ? std::string(static_cast<char*>(data), length) : std::string(); }
};
struct Files : FileInterface {
    struct File { std::vector<byte> bytes; size_t pos = 0; };
    FileHandle Open(const String& path) override {
        HostBuffer buffer(0, path.c_str());
        if (!buffer.data || buffer.length < 0) return 0;
        auto file = std::make_unique<File>();
        auto ptr = static_cast<byte*>(buffer.data);
        file->bytes.assign(ptr, ptr + buffer.length);
        return reinterpret_cast<FileHandle>(file.release());
    }
    void Close(FileHandle file) override { delete reinterpret_cast<File*>(file); }
    size_t Read(void* output, size_t size, FileHandle handle) override {
        auto& f = *reinterpret_cast<File*>(handle);
        size = std::min(size, f.bytes.size() - f.pos);
        if (size) std::memcpy(output, f.bytes.data() + f.pos, size);
        f.pos += size; return size;
    }
    bool Seek(FileHandle handle, long offset, int origin) override {
        auto& f = *reinterpret_cast<File*>(handle);
        int64_t start = origin == SEEK_SET ? 0 : origin == SEEK_CUR ? static_cast<int64_t>(f.pos) : origin == SEEK_END ? static_cast<int64_t>(f.bytes.size()) : -1;
        int64_t pos = start + offset;
        if (start < 0 || pos < 0 || pos > static_cast<int64_t>(f.bytes.size())) return false;
        f.pos = static_cast<size_t>(pos); return true;
    }
    size_t Tell(FileHandle file) override { return reinterpret_cast<File*>(file)->pos; }
    size_t Length(FileHandle file) override { return reinterpret_cast<File*>(file)->bytes.size(); }
};
struct System : SystemInterface {
    std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();
    double GetElapsedTime() override { return std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count(); }
    bool LogMessage(Log::Type level, const String& message) override { host.log(static_cast<int>(level), message.c_str()); return true; }
    void JoinPath(String& output, const String& document, const String& path) override {
        if (path.find(':') != String::npos || path.find('|') != String::npos) { output = asset_url(path); return; }
        auto canonical = asset_url(document); auto slash = canonical.rfind('/');
        auto pipe = canonical.find('|');
        if (!path.empty() && path[0] == '/') output = canonical.substr(0, pipe != String::npos ? pipe + 1 : 0) + path.substr(1);
        else output = canonical.substr(0, slash != String::npos ? slash + 1 : pipe != String::npos ? pipe + 1 : 0) + path;
    }
    int TranslateString(String& output, const String& input) override { HostBuffer buffer(2, input.c_str()); output = buffer.data ? buffer.text() : input; return output != input ? 1 : 0; }
    void SetClipboardText(const String& value) override { host.write(0, value.c_str()); }
    void GetClipboardText(String& value) override { HostBuffer buffer(3, ""); value = buffer.text(); }
    void SetMouseCursor(const String& value) override { host.write(1, value.c_str()); }
};
struct Renderer : RenderInterface_GL3 {
    TextureHandle LoadTexture(Vector2i& dimensions, const String& path) override {
        HostBuffer buffer(1, path.c_str());
        if (!buffer.data || buffer.width <= 0 || buffer.height <= 0 || int64_t(buffer.width) * buffer.height * 4 != buffer.length) return 0;
        dimensions = {buffer.width, buffer.height};
        return GenerateTexture({static_cast<const byte*>(buffer.data), static_cast<size_t>(buffer.length)}, dimensions);
    }
};
struct NullRenderer : RenderInterface {
    CompiledGeometryHandle CompileGeometry(Span<const Vertex>, Span<const int>) override { return 1; }
    void RenderGeometry(CompiledGeometryHandle, Vector2f, TextureHandle) override {}
    void ReleaseGeometry(CompiledGeometryHandle) override {}
    TextureHandle LoadTexture(Vector2i&, const String&) override { return 0; }
    TextureHandle GenerateTexture(Span<const byte>, Vector2i) override { return 1; }
    void ReleaseTexture(TextureHandle) override {}
    void EnableScissorRegion(bool) override {}
    void SetScissorRegion(Rectanglei) override {}
};
// The upstream renderer restores blend/stencil/depth enable, viewport and scissor.
// This guard additionally preserves all bindings and pixel transfer state it touches.
// It also wraps initialization, layout (font texture allocation), and destruction.
struct GLState {
    bool active;
    GLint program{}, vao{}, array{}, draw{}, read{}, texture_unit{}, textures[2]{}, samplers[2]{};
    GLint unpack{}, pack{}, unpack_align{}, unpack_row{}, unpack_pixels{}, unpack_rows{}, pack_align{}, pack_row{}, pack_pixels{}, pack_rows{};
    GLint polygon[2]{}; GLboolean depth_mask{}, srgb{}, rasterizer{};
    explicit GLState(bool needed = true) : active(needed && !headless_mode) {
        if (!active) return;
        glGetIntegerv(GL_CURRENT_PROGRAM, &program); glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &vao);
        glGetIntegerv(GL_ARRAY_BUFFER_BINDING, &array); glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &draw); glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &read);
        target_framebuffer = draw;
        glGetIntegerv(GL_ACTIVE_TEXTURE, &texture_unit);
        for (int i = 0; i < 2; i++) { glActiveTexture(GL_TEXTURE0 + i); glGetIntegerv(GL_TEXTURE_BINDING_2D, &textures[i]); glGetIntegeri_v(GL_SAMPLER_BINDING, i, &samplers[i]); glBindSampler(i, 0); }
        glActiveTexture(GL_TEXTURE0);
        glGetIntegerv(GL_PIXEL_UNPACK_BUFFER_BINDING, &unpack); glGetIntegerv(GL_PIXEL_PACK_BUFFER_BINDING, &pack);
        glGetIntegerv(GL_UNPACK_ALIGNMENT, &unpack_align); glGetIntegerv(GL_UNPACK_ROW_LENGTH, &unpack_row);
        glGetIntegerv(GL_UNPACK_SKIP_PIXELS, &unpack_pixels); glGetIntegerv(GL_UNPACK_SKIP_ROWS, &unpack_rows);
        glGetIntegerv(GL_PACK_ALIGNMENT, &pack_align); glGetIntegerv(GL_PACK_ROW_LENGTH, &pack_row);
        glGetIntegerv(GL_PACK_SKIP_PIXELS, &pack_pixels); glGetIntegerv(GL_PACK_SKIP_ROWS, &pack_rows);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0); glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1); glPixelStorei(GL_UNPACK_ROW_LENGTH, 0); glPixelStorei(GL_UNPACK_SKIP_PIXELS, 0); glPixelStorei(GL_UNPACK_SKIP_ROWS, 0);
        glPixelStorei(GL_PACK_ALIGNMENT, 1); glPixelStorei(GL_PACK_ROW_LENGTH, 0); glPixelStorei(GL_PACK_SKIP_PIXELS, 0); glPixelStorei(GL_PACK_SKIP_ROWS, 0);
        glGetIntegerv(GL_POLYGON_MODE, polygon); glPolygonMode(GL_FRONT_AND_BACK, GL_FILL);
        glGetBooleanv(GL_DEPTH_WRITEMASK, &depth_mask); glDepthMask(GL_FALSE);
        srgb = glIsEnabled(GL_FRAMEBUFFER_SRGB); glDisable(GL_FRAMEBUFFER_SRGB);
        rasterizer = glIsEnabled(GL_RASTERIZER_DISCARD); glDisable(GL_RASTERIZER_DISCARD);
    }
    ~GLState() {
        if (!active) return;
        glUseProgram(program); glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, array);
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER, draw); glBindFramebuffer(GL_READ_FRAMEBUFFER, read);
        for (int i = 0; i < 2; i++) { glActiveTexture(GL_TEXTURE0 + i); glBindTexture(GL_TEXTURE_2D, textures[i]); glBindSampler(i, samplers[i]); }
        glActiveTexture(texture_unit);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER, unpack); glBindBuffer(GL_PIXEL_PACK_BUFFER, pack);
        glPixelStorei(GL_UNPACK_ALIGNMENT, unpack_align); glPixelStorei(GL_UNPACK_ROW_LENGTH, unpack_row); glPixelStorei(GL_UNPACK_SKIP_PIXELS, unpack_pixels); glPixelStorei(GL_UNPACK_SKIP_ROWS, unpack_rows);
        glPixelStorei(GL_PACK_ALIGNMENT, pack_align); glPixelStorei(GL_PACK_ROW_LENGTH, pack_row); glPixelStorei(GL_PACK_SKIP_PIXELS, pack_pixels); glPixelStorei(GL_PACK_SKIP_ROWS, pack_rows);
        glPolygonMode(GL_FRONT_AND_BACK, polygon[0]); glDepthMask(depth_mask);
        if (srgb) glEnable(GL_FRAMEBUFFER_SRGB); else glDisable(GL_FRAMEBUFFER_SRGB);
        if (rasterizer) glEnable(GL_RASTERIZER_DISCARD); else glDisable(GL_RASTERIZER_DISCARD);
    }
};
std::unique_ptr<System> system_interface;
std::unique_ptr<Files> file_interface;
std::unique_ptr<RenderInterface> renderer;
struct Document { Context* context; ElementDocument* root; std::unordered_map<uint64_t, ObserverPtr<Element>> elements; int viewport_x = 0, viewport_y = 0; };
std::unordered_map<uint64_t, Document> documents;
struct QueuedEvent { NativeEvent header; String type, value, target_id; String mouse_x, mouse_y, button, wheel, modifiers, key; };
std::deque<QueuedEvent> events;
std::deque<uint64_t> detached_listeners;
QueuedEvent current_event;
Document& doc(uint64_t id) { check_thread(); auto it = documents.find(id); if (it == documents.end()) throw std::runtime_error("Document has been disposed."); return it->second; }
Element* element(Document& d, uint64_t id) {
    if (!id) return d.root;
    auto it = d.elements.find(id);
    if (it == d.elements.end() || !it->second) throw std::runtime_error("Element has been removed or disposed.");
    return it->second.get();
}
uint64_t remember(Document& d, Element* e) {
    if (!e) return 0;
    for (auto it = d.elements.begin(); it != d.elements.end();) {
        if (!it->second) it = d.elements.erase(it);
        else { if (it->second.get() == e) return it->first; ++it; }
    }
    auto id = next_id++; d.elements.emplace(id, e->GetObserverPtr()); return id;
}
struct Listener : EventListener {
    uint64_t id, document; ObserverPtr<Element> target; String type; bool capture;
    bool destroying = false;
    void ProcessEvent(Event& e) override {
        auto found = documents.find(document); if (found == documents.end()) return;
        auto* event_target = e.GetTargetElement();
        String value = e.GetParameter<String>("value", "");
        if (auto control = dynamic_cast<ElementFormControl*>(event_target)) value = control->GetValue();
        events.push_back({{id, document, remember(found->second, event_target)}, e.GetType(), value, event_target->GetId(),
            std::to_string(e.GetParameter<int>("mouse_x", 0)), std::to_string(e.GetParameter<int>("mouse_y", 0)),
            std::to_string(e.GetParameter<int>("button", -1)), std::to_string(e.GetParameter<float>("wheel_delta_y", e.GetParameter<float>("wheel_delta", 0))),
            std::to_string((e.GetParameter<bool>("ctrl_key", false) ? 1 : 0) | (e.GetParameter<bool>("shift_key", false) ? 2 : 0) | (e.GetParameter<bool>("alt_key", false) ? 4 : 0) | (e.GetParameter<bool>("meta_key", false) ? 8 : 0)),
            std::to_string(static_cast<int>(e.GetParameter<Input::KeyIdentifier>("key_identifier", Input::KI_UNKNOWN)))});
    }
    void OnDetach(Element*) override {
        target.reset();
        if (!destroying) detached_listeners.push_back(id);
    }
    ~Listener() { destroying = true; if (target) target->RemoveEventListener(type, this, capture); }
};
std::unordered_map<uint64_t, std::unique_ptr<Listener>> listeners;
void destroy_document(uint64_t id) {
    auto& d = doc(id);
    for (auto it = listeners.begin(); it != listeners.end();) { if (it->second->document == id) it = listeners.erase(it); else ++it; }
    events.erase(std::remove_if(events.begin(), events.end(), [id](const auto& e) { return e.header.document == id; }), events.end());
    auto name = d.context->GetName(); d.elements.clear();
    RemoveContext(name); documents.erase(id);
}
}
unsigned int vsrml_target_framebuffer() { return target_framebuffer; }
int VR_CALL vr_abi() { return 1; }
const char* VR_CALL vr_error() { return error.c_str(); }
int VR_CALL vr_init(const Callbacks* callbacks, int headless) {
    return guard([&]() {
        if (initialized) throw std::runtime_error("Only one VSRmlUi runtime may be active.");
        if (!callbacks || !callbacks->read || !callbacks->free || !callbacks->log || !callbacks->write) throw std::runtime_error("Missing host callbacks.");
        host = *callbacks; headless_mode = headless != 0;
        if (!headless_mode && !RmlGL3::Initialize()) throw std::runtime_error("OpenGL 3.3 initialization failed; a current game context is required.");
        GLState state;
        try {
            system_interface = std::make_unique<System>(); file_interface = std::make_unique<Files>();
            SetSystemInterface(system_interface.get()); SetFileInterface(file_interface.get());
            if (headless_mode) renderer = std::make_unique<NullRenderer>();
            else { auto gl = std::make_unique<Renderer>(); if (!*gl) throw std::runtime_error("RmlUi shader compilation failed."); renderer = std::move(gl); }
            SetRenderInterface(renderer.get());
            if (!Initialise()) throw std::runtime_error("RmlUi initialization failed.");
        } catch (...) {
            renderer.reset(); file_interface.reset(); system_interface.reset();
            SetRenderInterface(nullptr); SetFileInterface(nullptr); SetSystemInterface(nullptr);
            throw;
        }
        owner_thread = std::this_thread::get_id(); initialized = true; return 1;
    }, 0);
}
int VR_CALL vr_shutdown() {
    return guard([&]() { check_thread(); GLState state;
        while (!documents.empty()) destroy_document(documents.begin()->first);
        listeners.clear(); events.clear(); detached_listeners.clear(); current_event = {}; Shutdown(); initialized = false;
        renderer.reset(); file_interface.reset(); system_interface.reset(); return 1;
    }, 0);
}
uint64_t VR_CALL vr_load(const char* path, const char* markup, int width, int height, float scale) {
    return guard([&]() -> uint64_t { check_thread(); GLState state;
        auto id = next_id++; auto name = "vsrmlui-" + std::to_string(id);
        auto* c = CreateContext(name, {std::max(width, 1), std::max(height, 1)});
        if (!c) throw std::runtime_error("Unable to create RmlUi context.");
        c->SetDensityIndependentPixelRatio(scale);
        c->GetRootElement()->SetProperty("font-family", "vsrmlui-default");
        c->GetRootElement()->SetProperty("font-size", "16dp");
        c->Update();
        auto url = asset_url(path);
        auto* root = markup ? c->LoadDocumentFromMemory(markup, url) : c->LoadDocument(url);
        if (!root) { RemoveContext(name); throw std::runtime_error("Unable to load RML document. See the game log for parser errors."); }
        documents.emplace(id, Document{c, root, {}}); return id;
    }, uint64_t(0));
}
int64_t VR_CALL vr_doc(uint64_t id, int op, int a, int b, float value, const char* text) {
    return guard([&]() -> int64_t { auto& d = doc(id); GLState state(op != 13 && op != 14);
        switch (op) {
        case 0: destroy_document(id); break;
        case 1: d.root->Show(a ? ModalFlag::Modal : ModalFlag::None, b ? FocusFlag::None : FocusFlag::Auto); break;
        case 2: d.root->Hide(); d.context->ProcessMouseLeave(); break;
        case 3: d.context->SetDimensions({std::max(a, 1), std::max(b, 1)}); d.context->SetDensityIndependentPixelRatio(value); d.context->Update(); break;
        case 4: {
            if (headless_mode) d.context->Render();
            else { auto* r = static_cast<Renderer*>(renderer.get()); auto size = d.context->GetDimensions(); r->SetViewport(size.x, size.y, d.viewport_x, d.viewport_y); r->BeginFrame(); try { d.context->Render(); } catch (...) { r->EndFrame(); throw; } r->EndFrame(); }
            break;
        }
        case 5: return !d.context->ProcessMouseMove(a, b, static_cast<int>(value));
        case 6: return !d.context->ProcessMouseButtonDown(a, b);
        case 7: return !d.context->ProcessMouseButtonUp(a, b);
        case 8: return !d.context->ProcessMouseWheel(value, a);
        case 9: return !d.context->ProcessKeyDown(static_cast<Input::KeyIdentifier>(a), b);
        case 10: return !d.context->ProcessKeyUp(static_cast<Input::KeyIdentifier>(a), b);
        case 11: return !d.context->ProcessTextInput(text ? text : "");
        case 12: d.context->ProcessMouseLeave(); break;
        case 13: return dynamic_cast<ElementFormControl*>(d.context->GetFocusElement()) != nullptr;
        case 14: return d.context->IsMouseInteracting();
        case 16: d.viewport_x = a; d.viewport_y = b; break;
        case 15: if (auto* focus = d.context->GetFocusElement()) focus->Blur(); d.context->ProcessMouseLeave(); break;
        default: throw std::runtime_error("Unknown document operation.");
        }
        return 1;
    }, int64_t(-1));
}
uint64_t VR_CALL vr_element(uint64_t id, uint64_t handle, int op, const char* name, const char* value) {
    return guard([&]() -> uint64_t { auto& d = doc(id); GLState state; auto* e = element(d, handle);
        String n = name ? name : "", v = value ? value : "";
        switch (op) {
        case 0: return remember(d, e->GetElementById(n));
        case 1: return remember(d, e->QuerySelector(n));
        case 2:
            if (auto* select = dynamic_cast<ElementFormControlSelect*>(e)) select->RemoveAll();
            e->SetInnerRML(v); break;
        case 3: e->SetAttribute(n, v); break;
        case 4: e->RemoveAttribute(n); break;
        case 5: if (!e->SetProperty(n, v)) throw std::runtime_error("Invalid RCSS property or value."); break;
        case 6: e->RemoveProperty(n); break;
        case 7: e->SetClassNames(v); break;
        case 8: e->SetClass(n, v == "1"); break;
        case 9: { auto* c = dynamic_cast<ElementFormControl*>(e); if (!c) throw std::runtime_error("Element is not a form control."); c->SetValue(v); break; }
        case 10: e->Focus(); break;
        case 11: e->Blur(); break;
        case 12: { auto child = d.root->CreateElement(n); if (!child) throw std::runtime_error("Invalid element tag."); auto* ptr = e->AppendChild(std::move(child)); return remember(d, ptr); }
        case 13: if (e == d.root || !e->GetParentNode()) throw std::runtime_error("Cannot remove a document root."); e->GetParentNode()->RemoveChild(e); break;
        case 14: { Dictionary params; params["value"] = v; e->DispatchEvent(n, params); break; }
        case 15: e->SetInnerRML(""); e->AppendChild(d.root->CreateTextNode(v)); break;
        case 16: d.context->Update(); e->SetScrollLeft(std::stof(n)); e->SetScrollTop(std::stof(v)); break;
        default: throw std::runtime_error("Unknown element operation.");
        }
        return 1;
    }, uint64_t(0));
}
const char* VR_CALL vr_get(uint64_t id, uint64_t handle, int op, const char* name) {
    return guard([&]() -> const char* { auto& d = doc(id); auto* e = element(d, handle);
        switch (op) {
        case 0: result_string = e->GetInnerRML(); break;
        case 1: result_string = e->GetAttribute<String>(name ? name : "", ""); break;
        case 2: result_string = e->GetClassNames(); break;
        case 3: { auto* c = dynamic_cast<ElementFormControl*>(e); if (!c) throw std::runtime_error("Element is not a form control."); result_string = c->GetValue(); break; }
        case 4: result_string = e->GetId(); break;
        case 5: result_string = e->GetTagName(); break;
        case 6: { auto p = e->GetAbsoluteOffset(BoxArea::Border); auto size = e->GetBox().GetSize(BoxArea::Border);
            result_string = std::to_string(p.x) + "," + std::to_string(p.y) + "," + std::to_string(size.x) + "," + std::to_string(size.y) + "," + std::to_string(e->GetScrollLeft()) + "," + std::to_string(e->GetScrollTop()); break; }
        default: throw std::runtime_error("Unknown element getter.");
        } return result_string.c_str();
    }, static_cast<const char*>(nullptr));
}
int VR_CALL vr_query_all(uint64_t id, uint64_t handle, const char* selector, uint64_t* output, int capacity) {
    return guard([&]() { check_thread(); if (!output || capacity < 0) throw std::runtime_error("Invalid query output buffer."); auto& d = doc(id); auto* e = element(d, handle); ElementList matches; e->QuerySelectorAll(matches, selector ? selector : ""); int count = std::min<int>(capacity, static_cast<int>(matches.size())); for (int i = 0; i < count; ++i) output[i] = remember(d, matches[i]); return static_cast<int>(matches.size()); }, -1);
}
int VR_CALL vr_font(const char* path, const char* family, int weight, int italic, int fallback) {
    return guard([&]() { check_thread(); GLState state;
        if (!LoadFontFace(asset_url(path), family, italic ? Style::FontStyle::Italic : Style::FontStyle::Normal, static_cast<Style::FontWeight>(weight), fallback != 0)) throw std::runtime_error("Unable to load font."); return 1;
    }, 0);
}
uint64_t VR_CALL vr_listen(uint64_t id, uint64_t handle, const char* type, int capture) {
    return guard([&]() -> uint64_t { auto& d = doc(id); auto* e = element(d, handle); auto l = std::make_unique<Listener>();
        auto token = next_id++; l->id = token; l->document = id; l->target = e->GetObserverPtr(); l->type = type; l->capture = capture != 0;
        e->AddEventListener(type, l.get(), l->capture); listeners.emplace(token, std::move(l)); return token;
    }, uint64_t(0));
}
int VR_CALL vr_unlisten(uint64_t id) { return guard([&]() { check_thread(); listeners.erase(id); return 1; }, 0); }
int VR_CALL vr_poll(NativeEvent* output) {
    return guard([&]() {
        check_thread();
        if (!events.empty()) { current_event = std::move(events.front()); events.pop_front(); *output = current_event.header; return 1; }
        if (!detached_listeners.empty()) {
            auto id = detached_listeners.front(); detached_listeners.pop_front();
            listeners.erase(id); *output = {id, 0, 0}; return 2;
        }
        return 0;
    }, -1);
}
const char* VR_CALL vr_event_value(int field) { switch (field) { case 0: return current_event.type.c_str(); case 1: return current_event.value.c_str(); case 2: return current_event.target_id.c_str();
case 3: return current_event.mouse_x.c_str(); case 4: return current_event.mouse_y.c_str(); case 5: return current_event.button.c_str(); case 6: return current_event.wheel.c_str(); case 7: return current_event.modifiers.c_str(); case 8: return current_event.key.c_str(); default: return ""; } }
