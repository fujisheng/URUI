#include <RmlUi/Core/TextInputHandler.h>
#include <RmlUi/Core/TextInputContext.h>
#include <chrono>
#include <map>
#include <tuple>

namespace urui_interaction
{
    static_assert(sizeof(UruiElementEvent) == 72, "Element event packet layout changed");
    UruiElementCallback callback = nullptr;
    int callback_depth = 0;
    bool pending_destroy = false, pending_unload = false;
    bool Composing();
    UruiElementEvent input = {};
    const char* EventName(int type)
    {
        static const char* names[] = {"", "click", "urui-pointerdown", "urui-pointermove", "urui-pointerup",
            "urui-pointercancel", "urui-pointerenter", "urui-pointerleave", "urui-wheel", "keydown", "keyup",
            "focus", "blur", "change", "textinput", "scroll", "urui-capturelost"};
        return type > 0 && type < static_cast<int>(std::size(names)) ? names[type] : nullptr;
    }

    struct Subscription : Rml::EventListener
    {
        Rml::ObserverPtr<Rml::Element> element;
        uint64_t token;
        int type, capture;
        bool enabled = true;
        bool whole_path = false;

        Subscription(Rml::Element* el, uint64_t id, int event_type, int phase)
            : element(el->GetObserverPtr()), token(id), type(event_type), capture(phase) {}

        void ProcessEvent(Rml::Event& event) override
        {
            if (!enabled || !callback || !element || pending_destroy || pending_unload || (type == 13 && Composing())) return;
            auto payload = input;
            payload.target = element_references.Track(event.GetTargetElement(), document);
            payload.current = token;
            payload.type = type;
            payload.phase = static_cast<int>(event.GetPhase()) | (capture ? 8 : 0) | (whole_path ? 16 : 0);
            payload.key = type == 9 || type == 10 ? input.key : 0;
            auto text = event.GetParameter<Rml::String>(type == 14 ? "text" : "value", "");
            payload.text = text.c_str();
            if (!payload.target) return;
            ++callback_depth;
            int result = callback(&payload);
            --callback_depth;
            if ((result & 2) || pending_destroy || pending_unload) event.StopImmediatePropagation();
            else if (result & 1) event.StopPropagation();
        }
    };
    std::map<std::tuple<uint64_t, int, int>, std::unique_ptr<Subscription>> subscriptions;

    struct Pointer
    {
        Rml::ObserverPtr<Rml::Element> pressed, hover, capture;
        float x = 0, y = 0;
        int kind = 0, buttons = 0;
    };
    std::map<int, Pointer> pointers;
    int primary_touch = -1;

    class TextHandler : public Rml::TextInputHandler
    {
    public:
        Rml::TextInputContext* active = nullptr;
        Rml::ObserverPtr<Rml::Element> owner;
        bool composing = false;
        int start = 0, end = 0, original_end = 0;
        Rml::String original;
        void OnActivate(Rml::TextInputContext* value) override
        {
            active = value; composing = false;
            auto* focused = context ? context->GetFocusElement() : nullptr;
            owner = focused ? focused->GetObserverPtr() : Rml::ObserverPtr<Rml::Element>{};
        }
        void OnDeactivate(Rml::TextInputContext* value) override
        {
            if (active == value)
            {
                if (composing && owner)
                {
                    value->SetCompositionRange(0, 0);
                    if (auto* control = rmlui_dynamic_cast<Rml::ElementFormControl*>(owner.get())) control->SetValue(original);
                }
                active = nullptr; composing = false; original.clear(); owner = {};
            }
        }
        void OnDestroy(Rml::TextInputContext* value) override
        {
            if (active == value) { active = nullptr; composing = false; original.clear(); owner = {}; }
        }
    } text_handler;

    bool Composing() { return text_handler.composing; }

    void CancelCompositionFor(Rml::Element* element)
    {
        if (text_handler.composing && text_handler.owner.get() == element) urui_text_composition("", 2);
    }

    class System : public Rml::SystemInterface
    {
    public:
        Rml::String clipboard;
        double GetElapsedTime() override
        {
            return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }
        void SetClipboardText(const Rml::String& value) override { clipboard = value; }
        void GetClipboardText(Rml::String& value) override { value = clipboard; }
    } system;

    void Install()
    {
        Rml::SetSystemInterface(&system);
        Rml::SetTextInputHandler(&text_handler);
    }

    int Modifiers(int value)
    {
        int command_shortcuts = 0;
#if defined(__APPLE__)
        if (value & 8) command_shortcuts = Rml::Input::KM_CTRL;
#endif
        return ((value & 1) ? Rml::Input::KM_SHIFT : 0) | ((value & 2) ? Rml::Input::KM_CTRL : 0)
            | ((value & 4) ? Rml::Input::KM_ALT : 0) | ((value & 8) ? Rml::Input::KM_META : 0) | command_shortcuts;
    }

    Rml::Input::KeyIdentifier Key(int value)
    {
        using namespace Rml::Input;
        if (value >= 97 && value <= 122) return static_cast<KeyIdentifier>(KI_A + value - 97);
        if (value >= 48 && value <= 57) return static_cast<KeyIdentifier>(KI_0 + value - 48);
        if (value >= 282 && value <= 296) return static_cast<KeyIdentifier>(KI_F1 + value - 282);
        if (value >= 256 && value <= 265) return static_cast<KeyIdentifier>(KI_NUMPAD0 + value - 256);
        switch (value)
        {
        case 8: return KI_BACK; case 9: return KI_TAB; case 13: return KI_RETURN;
        case 27: return KI_ESCAPE; case 32: return KI_SPACE; case 127: return KI_DELETE;
        case 273: return KI_UP; case 274: return KI_DOWN; case 275: return KI_RIGHT; case 276: return KI_LEFT;
        case 277: return KI_INSERT; case 278: return KI_HOME; case 279: return KI_END;
        case 280: return KI_PRIOR; case 281: return KI_NEXT;
        case 266: return KI_DECIMAL; case 267: return KI_DIVIDE; case 268: return KI_MULTIPLY;
        case 269: return KI_SUBTRACT; case 270: return KI_ADD; case 271: return KI_NUMPADENTER;
        case 272: return KI_OEM_NEC_EQUAL;
        case 300: return KI_NUMLOCK; case 301: return KI_CAPITAL; case 302: return KI_SCROLL;
        case 303: return KI_RSHIFT; case 304: return KI_LSHIFT;
        case 305: return KI_RCONTROL; case 306: return KI_LCONTROL;
        case 307: return KI_RMENU; case 308: return KI_LMENU;
        case 309: case 312: return KI_RWIN; case 310: case 311: return KI_LWIN;
        case 315: return KI_HELP; case 316: return KI_SNAPSHOT; case 318: return KI_PAUSE; case 319: return KI_APPS;
        case 59: return KI_OEM_1; case 61: return KI_OEM_PLUS; case 44: return KI_OEM_COMMA;
        case 45: return KI_OEM_MINUS; case 46: return KI_OEM_PERIOD; case 47: return KI_OEM_2;
        case 96: return KI_OEM_3; case 91: return KI_OEM_4; case 92: return KI_OEM_5;
        case 93: return KI_OEM_6; case 39: return KI_OEM_7;
        default: return KI_UNKNOWN;
        }
    }

    bool Send(Rml::Element* target, int type)
    {
        if (!target || !document || pending_destroy || pending_unload) return false;
        Rml::Dictionary parameters{{"pointer_id", Rml::Variant(input.pointer)}, {"mouse_x", Rml::Variant(input.x)},
            {"mouse_y", Rml::Variant(input.y)}, {"button", Rml::Variant(input.button)}};
        return target->DispatchEvent(EventName(type), parameters, true, true);
    }

    void Reset()
    {
        for (auto& item : subscriptions)
        {
            auto& value = *item.second;
            if (value.element && value.enabled)
                value.element->RemoveEventListener(EventName(value.type), &value, value.capture != 0);
        }
        subscriptions.clear();
        pointers.clear(); primary_touch = -1;
        text_handler.active = nullptr; text_handler.composing = false;
        text_handler.owner = {};
        text_handler.original.clear();
    }
}

extern "C"
{
    int urui_set_event_callback(UruiElementCallback value)
    {
        if (context && std::this_thread::get_id() != context_thread) return -3;
        urui_interaction::callback = value; return 1;
    }

    int urui_document_listen(void)
    try
    {
        if (!urui_interaction::Owner() || !document) return -3;
        const auto token = element_references.Track(document, document);
        for (int type = 1; type <= 16; ++type)
        {
            if (urui_element_listen(token, type, 1, 1) != 1) return -1;
            urui_interaction::subscriptions.at(std::make_tuple(token, type, 1))->whole_path = true;
        }
        return 1;
    }
    catch (...) { return -8; }

    int urui_element_listen(uint64_t token, int type, int capture, int enabled)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !urui_interaction::EventName(type) || (capture != 0 && capture != 1)) return -1;
        auto key = std::make_tuple(token, type, capture);
        auto found = urui_interaction::subscriptions.find(key);
        if (found == urui_interaction::subscriptions.end())
        {
            if (!enabled) return 1;
            auto value = std::make_unique<urui_interaction::Subscription>(el, token, type, capture);
            el->AddEventListener(urui_interaction::EventName(type), value.get(), capture != 0);
            urui_interaction::subscriptions.emplace(key, std::move(value));
        }
        else if (found->second->enabled != (enabled != 0))
        {
            auto& value = *found->second;
            value.enabled = enabled != 0;
            if (enabled) el->AddEventListener(urui_interaction::EventName(type), &value, capture != 0);
            else el->RemoveEventListener(urui_interaction::EventName(type), &value, capture != 0);
        }
        return 1;
    }
    catch (...) { return -8; }

    int urui_pointer_capture(int pointer, uint64_t token)
    try
    {
        if (!urui_interaction::Owner()) return -3;
        auto* el = token ? urui_elements::Get(token) : nullptr;
        if (token && !el) return -1;
        auto found = urui_interaction::pointers.find(pointer);
        if (found == urui_interaction::pointers.end()) return -2;
        auto old = found->second.capture;
        found->second.capture = el ? el->GetObserverPtr() : Rml::ObserverPtr<Rml::Element>{};
        if (old && old.get() != el) urui_interaction::Send(old.get(), 16);
        return 1;
    }
    catch (...) { return -8; }

    uint64_t urui_pointer_captured(int pointer)
    try
    {
        if (!urui_interaction::Owner()) return 0;
        auto found = urui_interaction::pointers.find(pointer);
        return found == urui_interaction::pointers.end() ? 0 : element_references.Track(found->second.capture.get(), document);
    }
    catch (...) { return 0; }

    int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers)
    try
    {
        using namespace urui_interaction;
        if (!Owner() || !document) return -3;
        if (callback_depth || phase < 0 || phase > 4 || kind < 0 || kind > 1 || pointer < 0 || button < 0 || button > 2
            || !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(dx) || !std::isfinite(dy)) return -1;
        input = {}; input.pointer = pointer; input.kind = kind; input.button = button; input.buttons = buttons;
        input.x = x; input.y = y; input.dx = dx; input.dy = dy; input.modifiers = modifiers;
        auto& state = pointers[pointer];
        state.x = x; state.y = y; state.kind = kind; state.buttons = buttons;
        auto* hit = context->GetElementAtPoint({x, y});
        auto* target = state.capture ? state.capture.get() : hit;
        auto held_target = target ? target->GetObserverPtr() : Rml::ObserverPtr<Rml::Element>{};
        if (state.hover.get() != hit && !state.capture)
        {
            auto old = state.hover;
            state.hover = hit ? hit->GetObserverPtr() : Rml::ObserverPtr<Rml::Element>{};
            if (old) Send(old.get(), 7);
            if (state.hover) Send(state.hover.get(), 6);
        }
        if (pending_destroy || pending_unload) return 1;
        bool allowed = Send(held_target.get(), phase == 4 ? 8 : phase == 0 ? 2 : phase == 1 ? 3 : phase == 2 ? 4 : 5);
        if (pending_destroy || pending_unload) return 1;
        if (phase == 0)
        {
            state.pressed = held_target;
            if (kind == 1 && primary_touch < 0) primary_touch = pointer;
        }
        const bool primary = kind == 0 || primary_touch == pointer;
        if (allowed && primary)
        {
            context->ProcessMouseMove(static_cast<int>(x), static_cast<int>(y), Modifiers(modifiers));
            if (phase == 0) context->ProcessMouseButtonDown(button, Modifiers(modifiers));
            else if (phase == 2) context->ProcessMouseButtonUp(button, Modifiers(modifiers));
            else if (phase == 3)
            {
                context->ProcessMouseLeave(); context->ProcessMouseButtonUp(button, Modifiers(modifiers));
            }
            else if (phase == 4) context->ProcessMouseWheel(Rml::Vector2f(dx, dy), Modifiers(modifiers));
        }
        else if (allowed && phase == 2 && held_target && state.pressed.get() == hit)
        {
            Send(held_target.get(), 1);
        }
        else if (primary && (phase == 2 || phase == 3))
        {
            context->ProcessMouseLeave();
            context->ProcessMouseButtonUp(button, Modifiers(modifiers));
        }
        if (phase == 2 || phase == 3)
        {
            state.pressed = {};
            auto captured = state.capture;
            state.capture = {};
            if (captured) Send(captured.get(), 16);
            if (kind == 1) pointers.erase(pointer);
            if (primary_touch == pointer) primary_touch = -1;
        }
        return 1;
    }
    catch (...) { return -8; }

    int urui_input_reset(void)
    try
    {
        if (!urui_interaction::Owner()) return -3;
        if (urui_interaction::callback_depth) return -5;
        std::vector<std::pair<int, urui_interaction::Pointer>> current(urui_interaction::pointers.begin(), urui_interaction::pointers.end());
        for (const auto& pair : current)
            urui_pointer_event(pair.first, pair.second.kind, 3, pair.second.x, pair.second.y, 0, 0, 0, 0, 0);
        urui_interaction::pointers.clear();
        context->ProcessMouseLeave();
        for (int button = 0; button < 3; ++button) context->ProcessMouseButtonUp(button, 0);
        if (document) document->Focus();
        return 1;
    }
    catch (...) { return -8; }

    int urui_cancel_pointers(uint64_t token)
    try
    {
        auto* owner = urui_elements::Get(token);
        if (!owner) return -1;
        if (urui_interaction::callback_depth) return -5;
        auto weak_owner = owner->GetObserverPtr();
        std::vector<std::pair<int, urui_interaction::Pointer>> current(urui_interaction::pointers.begin(), urui_interaction::pointers.end());
        for (const auto& pair : current)
        {
            auto* root = weak_owner.get();
            if (!root) break;
            auto* target = pair.second.capture ? pair.second.capture.get() : pair.second.pressed.get();
            if (target && (target == root || root->Contains(target)))
                urui_pointer_event(pair.first, pair.second.kind, 3, pair.second.x, pair.second.y, 0, 0, 0, 0, 0);
        }
        return 1;
    }
    catch (...) { return -8; }

    int urui_key_event(int key, int pressed, int modifiers)
    try
    {
        if (!urui_interaction::Owner()) return -3;
        auto mapped = urui_interaction::Key(key);
        if (mapped == Rml::Input::KI_UNKNOWN) return 0;
        urui_interaction::input = {};
        urui_interaction::input.pointer = -1;
        urui_interaction::input.key = key;
        urui_interaction::input.modifiers = modifiers;
        if (pressed) context->ProcessKeyDown(mapped, urui_interaction::Modifiers(modifiers));
        else context->ProcessKeyUp(mapped, urui_interaction::Modifiers(modifiers));
        return 1;
    }
    catch (...) { return -8; }

    int urui_text_input(const char* text)
    try
    {
        if (!urui_interaction::Owner() || !text) return -1;
        context->ProcessTextInput(Rml::String(text)); return 1;
    }
    catch (...) { return -8; }

    int urui_text_composition(const char* text, int operation)
    try
    {
        using namespace urui_interaction;
        if (!Owner() || !text || operation < 0 || operation > 2 || !text_handler.active) return -1;
        auto& handler = text_handler;
        auto* control = rmlui_dynamic_cast<Rml::ElementFormControl*>(context->GetFocusElement());
        if (!control) return -1;
        if (operation == 2)
        {
            if (handler.composing)
            {
                handler.active->SetCompositionRange(0, 0);
                control->SetValue(handler.original);
                handler.active->SetSelectionRange(handler.start, handler.original_end);
            }
            handler.composing = false; handler.original.clear(); return 1;
        }
        if (!handler.composing)
        {
            if (operation == 1) { context->ProcessTextInput(Rml::String(text)); return 1; }
            // OnActivate runs before Context commits the new focused element.
            // Composition begins after Focus returns, so bind to the actual control here.
            handler.owner = control->GetObserverPtr();
            handler.active->GetSelectionRange(handler.start, handler.end);
            handler.original_end = handler.end;
            handler.original = control->GetValue();
            handler.composing = true;
        }
        if (operation == 0)
        {
            const Rml::String preview(text);
            handler.active->SetText(preview, handler.start, handler.end);
            handler.end = handler.start + static_cast<int>(Rml::StringUtilities::LengthUTF8(preview));
            handler.active->SetCompositionRange(handler.start, handler.end);
        }
        else
        {
            handler.active->SetCompositionRange(handler.start, handler.end);
            handler.active->CommitComposition(Rml::String(text));
            handler.active->SetCompositionRange(0, 0);
            handler.active->SetCursorPosition(handler.start + static_cast<int>(Rml::StringUtilities::LengthUTF8(Rml::String(text))));
            const bool changed = control->GetValue() != handler.original;
            handler.composing = false; handler.original.clear();
            if (changed) control->DispatchEvent("change", {{"value", Rml::Variant(control->GetValue())}});
        }
        return 1;
    }
    catch (...) { return -8; }

    int urui_text_selection(int* start, int* end, int write)
    try
    {
        if (!urui_interaction::Owner() || !start || !end || !urui_interaction::text_handler.active) return -1;
        if (write)
        {
            if (*start < 0 || *end < *start) return -1;
            urui_interaction::text_handler.active->SetSelectionRange(*start, *end);
        }
        else urui_interaction::text_handler.active->GetSelectionRange(*start, *end);
        return 1;
    }
    catch (...) { return -8; }

    int urui_clipboard_write(const char* text)
    try { if (!urui_interaction::Owner() || !text) return -1; urui_interaction::system.clipboard = text; return 1; }
    catch (...) { return -8; }

    int urui_clipboard_read(char* out, int capacity, int* required)
    try { if (!urui_interaction::Owner()) return -3; return urui_interaction::Copy(urui_interaction::system.clipboard, out, capacity, required); }
    catch (...) { return -8; }

    int urui_process_deferred_operations(void)
    try
    {
        if (!urui_interaction::Owner() || urui_interaction::callback_depth) return -3;
        if (urui_interaction::pending_destroy)
        {
            urui_interaction::pending_destroy = urui_interaction::pending_unload = false; urui_destroy(); return 1;
        }
        if (urui_interaction::pending_unload)
        {
            urui_interaction::pending_unload = false; urui_unload_document(); return 1;
        }
        for (auto it = urui_interaction::subscriptions.begin(); it != urui_interaction::subscriptions.end();)
        {
            if (!it->second->element || !it->second->enabled) it = urui_interaction::subscriptions.erase(it);
            else ++it;
        }
        return 1;
    }
    catch (...) { return -8; }
}
