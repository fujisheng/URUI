#include <URUI/Core/TextInputHandler.h>
#include <URUI/Core/TextInputContext.h>
#include <chrono>
#include <map>
#include <tuple>
#include "Engine/Source/Core/ElementAnimation.h"

namespace URUI::Interaction
{
    static_assert(sizeof(URUIElementEvent) == 80, "Element event packet layout changed");
    URUIElementCallback callback = nullptr;
    int callbackDepth = 0;
    const URUI::Event* animationEvent = nullptr;
    struct AnimationEventScope
    {
        const URUI::Event* previous;
        AnimationEventScope(const URUI::Event& event) : previous(animationEvent)
        {
            animationEvent = &event;
        }
        ~AnimationEventScope()
        {
            animationEvent = previous;
        }
    };
    bool pendingDestroy = false;
    bool Composing();
    URUIElementEvent input = {};
    const char* EventName(int type)
    {
        static const char* names[] = {"",
                                      "click",
                                      "urui-pointerdown",
                                      "urui-pointermove",
                                      "urui-pointerup",
                                      "urui-pointercancel",
                                      "urui-pointerenter",
                                      "urui-pointerleave",
                                      "urui-wheel",
                                      "keydown",
                                      "keyup",
                                      "focus",
                                      "blur",
                                      "change",
                                      "textinput",
                                      "scroll",
                                      "urui-capturelost",
                                      "animationend",
                                      "transitionend",
                                      "urui-animation-created"};
        return type > 0 && type < static_cast<int>(std::size(names)) ? names[type] : nullptr;
    }

    struct CandidateEvents : URUI::EventListener
    {
        struct PendingEvent
        {
            URUI::ObserverPtr<URUI::Element> target;
            URUI::Dictionary parameters;
            URUIElementEvent input;
            int type;
        };
        URUIDocumentHandle owner;
        URUI::ObserverPtr<URUI::Element> root;
        std::vector<PendingEvent> events;
        int attachedTypes = 0;

        explicit CandidateEvents(URUIDocumentHandle owner) : owner(owner)
        {
        }
        ~CandidateEvents()
        {
            Detach();
        }
        void Attach(URUI::ElementDocument& document)
        {
            root = document.GetObserverPtr();
            for (int type = 1; EventName(type); ++type)
            {
                document.AddEventListener(EventName(type), this, true);
                attachedTypes = type;
            }
        }
        void Detach() noexcept
        {
            if (root)
            {
                for (int type = 1; type <= attachedTypes; ++type)
                {
                    root->RemoveEventListener(EventName(type), this, true);
                }
            }
            attachedTypes = 0;
        }
        void ProcessEvent(URUI::Event& event) override
        {
            for (int type = 1; EventName(type); ++type)
            {
                if (event.GetType() == EventName(type))
                {
                    events.push_back({event.GetTargetElement()->GetObserverPtr(), event.GetParameters(), input, type});
                    return;
                }
            }
        }
    };
    std::vector<std::unique_ptr<CandidateEvents>> pendingDocuments;
    bool CandidatePending(URUI::Element* root)
    {
        return std::any_of(pendingDocuments.begin(), pendingDocuments.end(),
                           [root](const auto& candidate) { return candidate->root.get() == root; });
    }

    struct Subscription : URUI::EventListener
    {
        URUI::ObserverPtr<URUI::Element> element;
        uint64_t token;
        URUIDocumentHandle owner;
        int type;
        int capture;
        bool enabled = true;
        bool wholePath = false;

        Subscription(URUI::Element* el, uint64_t id, int event_type, int phase, URUIDocumentHandle owner)
            : element(el->GetObserverPtr()), token(id), owner(owner), type(event_type), capture(phase)
        {
        }

        void ProcessEvent(URUI::Event& event) override
        {
            if (!enabled || !callback || !element || pendingDestroy || (type == 13 && Composing()))
            {
                return;
            }
            auto* document = URUI::Elements::Document(owner);
            if (!document || CandidatePending(document))
            {
                return;
            }
            if (wholePath)
            {
                bool interested = false;
                bool disabled = false;
                for (auto* current = event.GetTargetElement(); current; current = current->GetParentNode())
                {
                    interested |= elementReferences.EventInterest(current, type);
                    disabled |= current->HasAttribute("disabled");
                    if (current == document)
                    {
                        break;
                    }
                }
                if (!interested)
                {
                    if (disabled && type != 5 && type != 12 && type != 15 && type != 16 && type != 17 && type != 18 &&
                        type != 19)
                    {
                        event.StopImmediatePropagation();
                    }
                    return;
                }
            }
            auto payload = input;
            payload.document = owner;
            payload.target = elementReferences.Track(event.GetTargetElement(), document);
            payload.current = token;
            payload.type = type;
            payload.phase = static_cast<int>(event.GetPhase()) | (capture ? 8 : 0) | (wholePath ? 16 : 0);
            payload.key = type == 9 || type == 10 ? input.key : 0;
            auto text = event.GetParameter<URUI::String>(type >= 17 ? "property" : type == 14 ? "text" : "value", "");
            payload.text = text.c_str();
            if (!payload.target)
            {
                return;
            }
            AnimationEventScope animationScope(event);
            ++callbackDepth;
            int result = callback(&payload);
            --callbackDepth;
            if ((result & 2) || pendingDestroy)
            {
                event.StopImmediatePropagation();
            }
            else if (result & 1)
            {
                event.StopPropagation();
            }
        }
    };
    std::map<std::tuple<uint64_t, int, int>, std::unique_ptr<Subscription>> subscriptions;

    void FlushCandidateEvents()
    {
        if (callbackDepth || pendingDestroy || pendingDocuments.empty())
        {
            return;
        }
        std::vector<std::unique_ptr<CandidateEvents>> ready;
        ready.swap(pendingDocuments);
        for (auto& candidate : ready)
        {
            auto* state = documentRegistry ? documentRegistry->Find(candidate->owner) : nullptr;
            if (!state || state->Element() != candidate->root.get())
            {
                continue;
            }
            candidate->Detach();
            if (!state->listening)
            {
                continue;
            }
            if (urui_document_listen(candidate->owner) != 1)
            {
                throw std::runtime_error("Document event publication failed");
            }
            for (const auto& pending : candidate->events)
            {
                auto* target = pending.target.get();
                auto* document = state->Element();
                if (!target || !elementReferences.Attached(target, document) || pendingDestroy)
                {
                    continue;
                }
                URUI::Event event(target, URUI::EventId::Invalid, EventName(pending.type), pending.parameters, true);
                std::vector<URUI::ObserverPtr<URUI::Element>> path;
                for (auto* current = target; current; current = current->GetParentNode())
                {
                    path.push_back(current->GetObserverPtr());
                    if (current == document)
                    {
                        break;
                    }
                }
                const auto previous_input = input;
                input = pending.input;
                auto deliver = [&](URUI::Element* current, int capture)
                {
                    if (!current || !pending.target || !event.IsImmediatePropagating())
                    {
                        return;
                    }
                    const auto token = elementReferences.Track(current, document);
                    auto found = subscriptions.find(std::make_tuple(token, pending.type, capture));
                    if (found != subscriptions.end())
                    {
                        event.SetCurrentElement(current);
                        event.SetPhase(current == target ? URUI::EventPhase::Target
                                       : capture         ? URUI::EventPhase::Capture
                                                         : URUI::EventPhase::Bubble);
                        found->second->ProcessEvent(event);
                    }
                };
                bool reachedTarget = false;
                for (auto it = path.rbegin(); it != path.rend() && event.IsPropagating(); ++it)
                {
                    reachedTarget = it->get() == target;
                    deliver(it->get(), 1);
                }
                for (size_t i = 0; i < path.size() && (event.IsPropagating() || (i == 0 && reachedTarget)); ++i)
                {
                    deliver(path[i].get(), 0);
                }
                input = previous_input;
            }
        }
    }

    struct Pointer
    {
        URUI::ObserverPtr<URUI::Element> pressed;
        URUI::ObserverPtr<URUI::Element> hover;
        URUI::ObserverPtr<URUI::Element> capture;
        URUI::ObserverPtr<URUI::Element> gestureDocument;
        float x = 0;
        float y = 0;
        int kind = 0;
        int buttons = 0;
        bool nativeTouch = false;
        bool active = false;
        bool pollReady = false;
        int pollModifiers = 0;
        uint64_t pollRevision = 0;
        URUI::ObserverPtr<URUI::Element> pollCapture;
    };
    std::map<int, Pointer> pointers;
    int primaryTouch = -1;

    void CancelNativeTouch(int pointer, Pointer& state)
    {
        if (!state.nativeTouch)
        {
            return;
        }
        state.nativeTouch = false;
        if (!context)
        {
            return;
        }
        // URUI 的 touch cancel 经 mouse-up 收尾；先移出 hover，避免取消变成点击。
        context->ProcessMouseLeave();
        context->ProcessTouchCancel(URUI::TouchList{{static_cast<URUI::TouchId>(pointer), {state.x, state.y}}});
    }

    class TextHandler : public URUI::TextInputHandler
    {
    public:
        URUI::TextInputContext* active = nullptr;
        URUI::ObserverPtr<URUI::Element> owner;
        bool composing = false;
        int start = 0;
        int end = 0;
        int originalEnd = 0;
        URUI::String original;
        void OnActivate(URUI::TextInputContext* value) override
        {
            active = value;
            composing = false;
            auto* focused = context ? context->GetFocusElement() : nullptr;
            owner = focused ? focused->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        }
        void OnDeactivate(URUI::TextInputContext* value) override
        {
            if (active == value)
            {
                if (composing && owner)
                {
                    value->SetCompositionRange(0, 0);
                    if (auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(owner.get()))
                    {
                        control->SetValue(original);
                    }
                }
                active = nullptr;
                composing = false;
                original.clear();
                owner = {};
            }
        }
        void OnDestroy(URUI::TextInputContext* value) override
        {
            if (active == value)
            {
                active = nullptr;
                composing = false;
                original.clear();
                owner = {};
            }
        }
    } textHandler;

    bool Composing()
    {
        return textHandler.composing;
    }

    bool FocusReceivesInput()
    {
        auto* focused = context ? context->GetFocusElement() : nullptr;
        return !focused ||
               (focused->IsVisible(true) && !context->IsDocumentBlockedByModal(focused->GetOwnerDocument()));
    }

    void CancelCompositionFor(URUI::Element* element)
    {
        if (textHandler.composing && textHandler.owner.get() == element)
        {
            urui_text_composition("", 2);
        }
    }

    class System : public URUI::SystemInterface
    {
    public:
        double externalTime = 0;
        bool externalTimeEnabled = false;
        URUI::String clipboard;
        uint64_t clipboardVersion = 1;
        bool captureDiagnostics = false;
        std::vector<URUI::String> diagnostics;
        bool LogMessage(URUI::Log::Type type, const URUI::String& message) override
        {
            if (!captureDiagnostics)
            {
                return URUI::SystemInterface::LogMessage(type, message);
            }
            if (type <= URUI::Log::LT_WARNING)
            {
                diagnostics.push_back(message);
            }
            return true;
        }
        double GetElapsedTime() override
        {
            if (externalTimeEnabled)
            {
                return externalTime;
            }
            return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }
        void SetClipboardText(const URUI::String& value) override
        {
            if (clipboardVersion == UINT64_MAX)
            {
                throw std::overflow_error("clipboard version exhausted");
            }
            clipboard = value;
            ++clipboardVersion;
        }
        void GetClipboardText(URUI::String& value) override
        {
            value = clipboard;
        }
    } system;

    void ResetClock()
    {
        system.externalTimeEnabled = false;
    }

    void Install()
    {
        URUI::SetSystemInterface(&system);
        URUI::SetTextInputHandler(&textHandler);
    }

    int Modifiers(int value)
    {
        int commandShortcuts = 0;
#if defined(__APPLE__)
        if (value & 8)
        {
            commandShortcuts = URUI::Input::KM_CTRL;
        }
#endif
        return ((value & 1) ? URUI::Input::KM_SHIFT : 0) | ((value & 2) ? URUI::Input::KM_CTRL : 0) |
               ((value & 4) ? URUI::Input::KM_ALT : 0) | ((value & 8) ? URUI::Input::KM_META : 0) | commandShortcuts;
    }

    URUI::Input::KeyIdentifier Key(int value)
    {
        using namespace URUI::Input;
        if (value >= 97 && value <= 122)
        {
            return static_cast<KeyIdentifier>(KI_A + value - 97);
        }
        if (value >= 48 && value <= 57)
        {
            return static_cast<KeyIdentifier>(KI_0 + value - 48);
        }
        if (value >= 282 && value <= 296)
        {
            return static_cast<KeyIdentifier>(KI_F1 + value - 282);
        }
        if (value >= 256 && value <= 265)
        {
            return static_cast<KeyIdentifier>(KI_NUMPAD0 + value - 256);
        }
        switch (value)
        {
        case 8:
            return KI_BACK;
        case 9:
            return KI_TAB;
        case 13:
            return KI_RETURN;
        case 27:
            return KI_ESCAPE;
        case 32:
            return KI_SPACE;
        case 127:
            return KI_DELETE;
        case 273:
            return KI_UP;
        case 274:
            return KI_DOWN;
        case 275:
            return KI_RIGHT;
        case 276:
            return KI_LEFT;
        case 277:
            return KI_INSERT;
        case 278:
            return KI_HOME;
        case 279:
            return KI_END;
        case 280:
            return KI_PRIOR;
        case 281:
            return KI_NEXT;
        case 266:
            return KI_DECIMAL;
        case 267:
            return KI_DIVIDE;
        case 268:
            return KI_MULTIPLY;
        case 269:
            return KI_SUBTRACT;
        case 270:
            return KI_ADD;
        case 271:
            return KI_NUMPADENTER;
        case 272:
            return KI_OEM_NEC_EQUAL;
        case 300:
            return KI_NUMLOCK;
        case 301:
            return KI_CAPITAL;
        case 302:
            return KI_SCROLL;
        case 303:
            return KI_RSHIFT;
        case 304:
            return KI_LSHIFT;
        case 305:
            return KI_RCONTROL;
        case 306:
            return KI_LCONTROL;
        case 307:
            return KI_RMENU;
        case 308:
            return KI_LMENU;
        case 309:
        case 312:
            return KI_RWIN;
        case 310:
        case 311:
            return KI_LWIN;
        case 315:
            return KI_HELP;
        case 316:
            return KI_SNAPSHOT;
        case 318:
            return KI_PAUSE;
        case 319:
            return KI_APPS;
        case 59:
            return KI_OEM_1;
        case 61:
            return KI_OEM_PLUS;
        case 44:
            return KI_OEM_COMMA;
        case 45:
            return KI_OEM_MINUS;
        case 46:
            return KI_OEM_PERIOD;
        case 47:
            return KI_OEM_2;
        case 96:
            return KI_OEM_3;
        case 91:
            return KI_OEM_4;
        case 92:
            return KI_OEM_5;
        case 93:
            return KI_OEM_6;
        case 39:
            return KI_OEM_7;
        default:
            return KI_UNKNOWN;
        }
    }

    bool Send(URUI::Element* target, int type)
    {
        if (!target || !target->GetOwnerDocument() || !target->IsVisible(true) || pendingDestroy)
        {
            return false;
        }
        if (type != 5 && type != 7 && type != 16 && context->IsDocumentBlockedByModal(target->GetOwnerDocument()))
        {
            return false;
        }
        URUI::Dictionary parameters{{"pointer_id", URUI::Variant(input.pointer)},
                                    {"mouse_x", URUI::Variant(input.x)},
                                    {"mouse_y", URUI::Variant(input.y)},
                                    {"button", URUI::Variant(input.button)}};
        return target->DispatchEvent(EventName(type), parameters, true, true);
    }

    void Reset()
    {
        pendingDocuments.clear();
        for (auto& item : subscriptions)
        {
            auto& value = *item.second;
            if (value.element && value.enabled)
            {
                value.element->RemoveEventListener(EventName(value.type), &value, value.capture != 0);
            }
        }
        subscriptions.clear();
        for (auto& pair : pointers)
        {
            CancelNativeTouch(pair.first, pair.second);
        }
        pointers.clear();
        primaryTouch = -1;
        textHandler.active = nullptr;
        textHandler.composing = false;
        textHandler.owner = {};
        textHandler.original.clear();
    }

    void CancelDocumentInput(URUI::ElementDocument* document) noexcept
    {
        for (auto it = pointers.begin(); it != pointers.end();)
        {
            const auto pointer = it->first;
            auto& state = it->second;
            // Touch cancellation may erase its own entry.
            ++it;
            auto* target = state.capture ? state.capture.get() : state.pressed.get();
            if (state.gestureDocument.get() == document || (target && target->GetOwnerDocument() == document))
            {
                urui_pointer_event(pointer, state.kind, 3, state.x, state.y, 0, 0, 0, 0, 0);
            }
            auto current = pointers.find(pointer);
            if (current != pointers.end() && current->second.hover &&
                current->second.hover->GetOwnerDocument() == document)
            {
                auto hovered = current->second.hover;
                current->second.hover = {};
                try
                {
                    Send(hovered.get(), 7);
                }
                catch (...)
                {
                }
            }
        }
        if (textHandler.owner && textHandler.owner->GetOwnerDocument() == document)
        {
            urui_text_composition("", 2);
        }
    }

    void RetireDocument(URUI::ElementDocument* document) noexcept
    {
        CancelDocumentInput(document);
        if (auto resources = document->GetResourceContext())
        {
            resources->Retire();
        }
        pendingDocuments.erase(std::remove_if(pendingDocuments.begin(), pendingDocuments.end(),
                                              [document](const auto& candidate)
                                              { return candidate->root.get() == document; }),
                               pendingDocuments.end());
        const auto owner = documentRegistry ? documentRegistry->HandleOf(document) : 0;
        for (auto it = subscriptions.begin(); it != subscriptions.end();)
        {
            auto& value = *it->second;
            if (value.owner != owner && (!value.element || value.element->GetOwnerDocument() != document))
            {
                ++it;
                continue;
            }
            if (value.element && value.enabled)
            {
                value.element->RemoveEventListener(EventName(value.type), &value, value.capture != 0);
            }
            it = subscriptions.erase(it);
        }
    }

    void CancelBlockedDocumentInput() noexcept
    {
        if (!context)
        {
            return;
        }
        for (int i = 0; i < context->GetNumDocuments(); ++i)
        {
            auto* document = context->GetDocument(i);
            if (context->IsDocumentBlockedByModal(document))
            {
                CancelDocumentInput(document);
            }
        }
    }
}

extern "C"
{
    int urui_set_event_callback(URUIElementCallback value)
    {
        if (context && std::this_thread::get_id() != contextThread)
        {
            return -3;
        }
        URUI::Interaction::callback = value;
        return 1;
    }

    int urui_element_event_interest(URUIDocumentHandle owner, uint64_t token, uint32_t mask, uint64_t version)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        if (!URUI::Elements::Get(owner, token))
        {
            return -2;
        }
        return elementReferences.SetEventInterest(token, mask, version);
    }
    catch (...)
    {
        return -8;
    }

    int urui_document_listen(URUIDocumentHandle document_handle)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        auto* state = documentRegistry ? documentRegistry->Find(document_handle) : nullptr;
        if (!state)
        {
            return -2;
        }
        state->listening = true;
        auto* document = state->Element();
        if (!document || URUI::Interaction::CandidatePending(document))
        {
            return 1;
        }
        const auto token = elementReferences.Track(document, document);
        for (int type = 1; URUI::Interaction::EventName(type); ++type)
        {
            if (urui_element_listen(document_handle, token, type, 1, 1) != 1)
            {
                return -1;
            }
            URUI::Interaction::subscriptions.at(std::make_tuple(token, type, 1))->wholePath = true;
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_element_listen(URUIDocumentHandle document_handle, uint64_t token, int type, int capture, int enabled)
    try
    {
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el || !URUI::Interaction::EventName(type) || (capture != 0 && capture != 1))
        {
            return -1;
        }
        auto key = std::make_tuple(token, type, capture);
        auto found = URUI::Interaction::subscriptions.find(key);
        if (found == URUI::Interaction::subscriptions.end())
        {
            if (!enabled)
            {
                return 1;
            }
            auto value = std::make_unique<URUI::Interaction::Subscription>(el, token, type, capture, document_handle);
            el->AddEventListener(URUI::Interaction::EventName(type), value.get(), capture != 0);
            URUI::Interaction::subscriptions.emplace(key, std::move(value));
        }
        else if (found->second->enabled != (enabled != 0))
        {
            auto& value = *found->second;
            value.enabled = enabled != 0;
            if (enabled)
            {
                el->AddEventListener(URUI::Interaction::EventName(type), &value, capture != 0);
            }
            else
            {
                el->RemoveEventListener(URUI::Interaction::EventName(type), &value, capture != 0);
            }
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_pointer_capture(URUIDocumentHandle document_handle, int pointer, uint64_t token)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        auto* document = URUI::Elements::Document(document_handle);
        if (!document)
        {
            return -1;
        }
        auto* el = token ? URUI::Elements::Get(document_handle, token) : nullptr;
        if (token && (!el || !el->IsVisible(true) || context->IsDocumentBlockedByModal(document) ||
                      (el != document && !document->Contains(el))))
        {
            return -1;
        }
        auto found = URUI::Interaction::pointers.find(pointer);
        if (found == URUI::Interaction::pointers.end())
        {
            return -2;
        }
        auto& state = found->second;
        if (token && !state.active)
        {
            return -2;
        }
        auto* origin = state.capture ? state.capture->GetOwnerDocument() : state.gestureDocument.get();
        if (origin != document)
        {
            return -1;
        }
        auto old = found->second.capture;
        found->second.capture = el ? el->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        if (old && old.get() != el)
        {
            URUI::Interaction::Send(old.get(), 16);
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    uint64_t urui_pointer_captured(URUIDocumentHandle document_handle, int pointer)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!URUI::Interaction::Owner())
        {
            return 0;
        }
        auto found = URUI::Interaction::pointers.find(pointer);
        return found == URUI::Interaction::pointers.end()
                   ? 0
                   : elementReferences.Track(found->second.capture.get(), document);
    }
    catch (...)
    {
        return 0;
    }

    int urui_pointer_poll(int pointer, int kind, float x, float y, int buttons, int modifiers)
    try
    {
        using namespace URUI::Interaction;
        if (!Owner())
        {
            return -3;
        }
        if (callbackDepth || pointer < 0 || kind < 0 || kind > 1 || buttons < 0 || buttons > 7 || modifiers < 0 ||
            modifiers > 15 || !std::isfinite(x) || !std::isfinite(y))
        {
            return -1;
        }
        const auto found = pointers.find(pointer);
        const auto revision = context->GetHitTestRevision();
        if (found != pointers.end())
        {
            const auto& state = found->second;
            if (state.pollReady && revision != UINT64_MAX && state.pollRevision == revision && state.x == x &&
                state.y == y && state.kind == kind && state.buttons == buttons && state.pollModifiers == modifiers &&
                state.capture.get() == state.pollCapture.get())
            {
                return 1;
            }
        }
        // 记录实际参与本轮分发的状态；回调内转移捕获或修改命中树后，
        // 不能把新状态误记为已经处理，否则静止坐标永远不会通知新目标。
        URUI::ObserverPtr<URUI::Element> dispatchedCapture;
        if (found != pointers.end())
        {
            dispatchedCapture = found->second.capture;
        }
        const auto result = urui_pointer_event(pointer, kind, 1, x, y, 0, buttons, 0, 0, modifiers);
        if (result == 1 && context && !pendingDestroy)
        {
            auto current = pointers.find(pointer);
            if (current != pointers.end())
            {
                auto& state = current->second;
                state.pollReady = true;
                state.pollRevision = revision;
                state.pollModifiers = modifiers;
                state.pollCapture = dispatchedCapture;
            }
        }
        return result;
    }
    catch (...)
    {
        return -8;
    }

    int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx,
                           float dy, int modifiers)
    try
    {
        using namespace URUI::Interaction;
        if (!Owner())
        {
            return -3;
        }
        if (callbackDepth || phase < 0 || phase > 4 || kind < 0 || kind > 1 || pointer < 0 || button < 0 ||
            button > 2 || !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(dx) || !std::isfinite(dy))
        {
            return -1;
        }
        input = {};
        input.pointer = pointer;
        input.kind = kind;
        input.button = button;
        input.buttons = buttons;
        input.x = x;
        input.y = y;
        input.dx = dx;
        input.dy = dy;
        input.modifiers = modifiers;
        auto& state = pointers[pointer];
        state.pollReady = false;
        if (state.nativeTouch && (kind != 1 || phase == 0))
        {
            CancelNativeTouch(pointer, state);
        }
        if (pendingDestroy)
        {
            return 1;
        }
        state.x = x;
        state.y = y;
        state.kind = kind;
        state.buttons = buttons;
        auto* hit = context->GetElementAtPoint({x, y});
        // Context 根节点只是 Surface 的空白命中面，不是可接收作者事件的页面节点。
        if (hit == context->GetRootElement())
        {
            hit = nullptr;
        }
        auto* target = state.capture ? state.capture.get() : phase == 3 ? state.pressed.get() : hit;
        auto heldTarget = target ? target->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        if (phase == 0)
        {
            state.active = true;
            state.pressed = heldTarget;
            auto* owner = target ? target->GetOwnerDocument() : nullptr;
            state.gestureDocument = owner ? owner->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        }
        else if (phase == 2 || phase == 3)
        {
            state.active = false;
        }
        if (state.hover.get() != hit && !state.capture)
        {
            auto old = state.hover;
            state.hover = hit ? hit->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
            if (old)
            {
                Send(old.get(), 7);
            }
            if (state.hover)
            {
                Send(state.hover.get(), 6);
            }
        }
        if (pendingDestroy)
        {
            return 1;
        }
        // 空白区域没有可取消默认行为的目标；原生鼠标仍需更新以继续/结束拖拽。
        bool allowed = !heldTarget || Send(heldTarget.get(), phase == 4   ? 8
                                                             : phase == 0 ? 2
                                                             : phase == 1 ? 3
                                                             : phase == 2 ? 4
                                                                          : 5);
        if (pendingDestroy)
        {
            return 1;
        }
        if (phase == 0)
        {
            if (kind == 1 && primaryTouch < 0)
            {
                primaryTouch = pointer;
            }
        }
        const bool primary = kind == 0 || primaryTouch == pointer;
        if (allowed && primary)
        {
            if (kind == 1 && phase != 4)
            {
                const URUI::Touch touch{static_cast<URUI::TouchId>(pointer), {x, y}};
                if (phase == 0)
                {
                    state.nativeTouch = true;
                    context->ProcessTouchStart(URUI::TouchList{touch}, Modifiers(modifiers));
                }
                else if (phase == 1 && state.nativeTouch)
                {
                    context->ProcessTouchMove(URUI::TouchList{touch}, Modifiers(modifiers));
                }
                else if (phase == 2 && state.nativeTouch)
                {
                    state.nativeTouch = false;
                    context->ProcessTouchEnd(URUI::TouchList{touch}, Modifiers(modifiers));
                }
                else if (phase == 3)
                {
                    CancelNativeTouch(pointer, state);
                }
            }
            else
            {
                context->ProcessMouseMove(static_cast<int>(x), static_cast<int>(y), Modifiers(modifiers));
                if (phase == 0)
                {
                    context->ProcessMouseButtonDown(button, Modifiers(modifiers));
                }
                else if (phase == 2)
                {
                    context->ProcessMouseButtonUp(button, Modifiers(modifiers));
                }
                else if (phase == 3)
                {
                    context->ProcessMouseLeave();
                    context->ProcessMouseButtonUp(button, Modifiers(modifiers));
                }
                else if (phase == 4)
                {
                    context->ProcessMouseWheel(URUI::Vector2f(dx, dy), Modifiers(modifiers));
                }
            }
        }
        else if (allowed && phase == 2 && heldTarget && state.pressed.get() == hit)
        {
            Send(heldTarget.get(), 1);
        }
        else if (primary && (phase == 2 || phase == 3))
        {
            if (state.nativeTouch)
            {
                CancelNativeTouch(pointer, state);
            }
            else
            {
                context->ProcessMouseLeave();
                context->ProcessMouseButtonUp(button, Modifiers(modifiers));
            }
        }
        if (phase == 2 || phase == 3)
        {
            state.pressed = {};
            auto captured = state.capture;
            state.capture = {};
            if (captured)
            {
                Send(captured.get(), 16);
            }
            state.gestureDocument = {};
            if (kind == 1)
            {
                pointers.erase(pointer);
            }
            if (primaryTouch == pointer)
            {
                primaryTouch = -1;
            }
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_input_reset(void)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        std::vector<std::pair<int, URUI::Interaction::Pointer>> current(URUI::Interaction::pointers.begin(),
                                                                        URUI::Interaction::pointers.end());
        for (const auto& pair : current)
        {
            urui_pointer_event(pair.first, pair.second.kind, 3, pair.second.x, pair.second.y, 0, 0, 0, 0, 0);
        }
        URUI::Interaction::pointers.clear();
        context->ProcessMouseLeave();
        for (int button = 0; button < 3; ++button)
        {
            context->ProcessMouseButtonUp(button, 0);
        }
        auto* focused = context->GetFocusElement();
        if (auto* owner = focused ? focused->GetOwnerDocument() : nullptr)
        {
            owner->Focus();
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_cancel_pointers(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* owner = URUI::Elements::Get(document_handle, token);
        if (!owner)
        {
            return -1;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        auto weakOwner = owner->GetObserverPtr();
        std::vector<std::pair<int, URUI::Interaction::Pointer>> current(URUI::Interaction::pointers.begin(),
                                                                        URUI::Interaction::pointers.end());
        for (const auto& pair : current)
        {
            auto* root = weakOwner.get();
            if (!root)
            {
                break;
            }
            auto* target = pair.second.capture ? pair.second.capture.get() : pair.second.pressed.get();
            if (target && (target == root || root->Contains(target)))
            {
                urui_pointer_event(pair.first, pair.second.kind, 3, pair.second.x, pair.second.y, 0, 0, 0, 0, 0);
            }
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_key_event(int key, int pressed, int modifiers)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        if (!URUI::Interaction::FocusReceivesInput())
        {
            return 0;
        }
        auto mapped = URUI::Interaction::Key(key);
        if (mapped == URUI::Input::KI_UNKNOWN)
        {
            return 0;
        }
        URUI::Interaction::input = {};
        URUI::Interaction::input.pointer = -1;
        URUI::Interaction::input.key = key;
        URUI::Interaction::input.modifiers = modifiers;
        if (pressed)
        {
            context->ProcessKeyDown(mapped, URUI::Interaction::Modifiers(modifiers));
        }
        else
        {
            context->ProcessKeyUp(mapped, URUI::Interaction::Modifiers(modifiers));
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_text_input(const char* text)
    try
    {
        if (!URUI::Interaction::Owner() || !text)
        {
            return -1;
        }
        if (!URUI::Interaction::FocusReceivesInput())
        {
            return 0;
        }
        context->ProcessTextInput(URUI::String(text));
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_text_composition(const char* text, int operation)
    try
    {
        using namespace URUI::Interaction;
        if (!Owner() || !text || operation < 0 || operation > 2 || !textHandler.active)
        {
            return -1;
        }
        if (operation != 2 && !FocusReceivesInput())
        {
            return 0;
        }
        auto& handler = textHandler;
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(context->GetFocusElement());
        if (!control)
        {
            return -1;
        }
        if (operation == 2)
        {
            if (handler.composing)
            {
                handler.active->SetCompositionRange(0, 0);
                control->SetValue(handler.original);
                handler.active->SetSelectionRange(handler.start, handler.originalEnd);
            }
            handler.composing = false;
            handler.original.clear();
            return 1;
        }
        if (!handler.composing)
        {
            if (operation == 1)
            {
                context->ProcessTextInput(URUI::String(text));
                return 1;
            }
            // OnActivate runs before Context commits the new focused element.
            // Composition begins after Focus returns, so bind to the actual control here.
            handler.owner = control->GetObserverPtr();
            handler.active->GetSelectionRange(handler.start, handler.end);
            handler.originalEnd = handler.end;
            handler.original = control->GetValue();
            handler.composing = true;
        }
        if (operation == 0)
        {
            const URUI::String preview(text);
            handler.active->SetText(preview, handler.start, handler.end);
            handler.end = handler.start + static_cast<int>(URUI::StringUtilities::LengthUTF8(preview));
            handler.active->SetCompositionRange(handler.start, handler.end);
        }
        else
        {
            handler.active->SetCompositionRange(handler.start, handler.end);
            handler.active->CommitComposition(URUI::String(text));
            handler.active->SetCompositionRange(0, 0);
            handler.active->SetCursorPosition(handler.start +
                                              static_cast<int>(URUI::StringUtilities::LengthUTF8(URUI::String(text))));
            const bool changed = control->GetValue() != handler.original;
            handler.composing = false;
            handler.original.clear();
            if (changed)
            {
                control->DispatchEvent("change", {{"value", URUI::Variant(control->GetValue())}});
            }
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_text_selection(int* start, int* end, int write)
    try
    {
        if (!URUI::Interaction::Owner() || !start || !end || !URUI::Interaction::textHandler.active)
        {
            return -1;
        }
        if (write)
        {
            if (*start < 0 || *end < *start)
            {
                return -1;
            }
            URUI::Interaction::textHandler.active->SetSelectionRange(*start, *end);
        }
        else
        {
            URUI::Interaction::textHandler.active->GetSelectionRange(*start, *end);
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    uint64_t urui_clipboard_version()
    {
        return URUI::Interaction::Owner() ? URUI::Interaction::system.clipboardVersion : 0;
    }

    int urui_clipboard_write(const char* text)
    try
    {
        if (!URUI::Interaction::Owner() || !text)
        {
            return -1;
        }
        URUI::Interaction::system.clipboard = text;
        return 1;
    }
    catch (...)
    {
        return -8;
    }

    int urui_clipboard_read(char* out, int capacity, int* required)
    try
    {
        if (!URUI::Interaction::Owner())
        {
            return -3;
        }
        return URUI::Interaction::Copy(URUI::Interaction::system.clipboard, out, capacity, required);
    }
    catch (...)
    {
        return -8;
    }

    int urui_process_deferred_operations(void)
    try
    {
        if (!URUI::Interaction::Owner() || URUI::Interaction::callbackDepth)
        {
            return -3;
        }
        if (URUI::Interaction::pendingDestroy)
        {
            URUI::Interaction::pendingDestroy = false;
            urui_destroy();
            return 1;
        }
        URUI::Interaction::FlushCandidateEvents();
        for (auto it = URUI::Interaction::subscriptions.begin(); it != URUI::Interaction::subscriptions.end();)
        {
            if (!it->second->element || !it->second->enabled)
            {
                it = URUI::Interaction::subscriptions.erase(it);
            }
            else
            {
                ++it;
            }
        }
        return 1;
    }
    catch (...)
    {
        return -8;
    }
}

extern "C"
{
    int urui_ui_clock_api_version(void)
    {
        return 1;
    }
    int urui_set_ui_time(double now)
    {
        if (!std::isfinite(now) || now < 0)
        {
            return 0;
        }
        URUI::Interaction::system.externalTime = now;
        URUI::Interaction::system.externalTimeEnabled = true;
        return 1;
    }
    int urui_animation_api_version(void)
    {
        return 1;
    }
    uint64_t urui_animation_next_identity(void)
    {
        return URUI::AnimationIdentityCounter();
    }
    int urui_animation_current_event(URUIDocumentHandle document_handle, char* name, int capacity, uint64_t* identity,
                                     int* iterations)
    {
        if (!name || capacity <= 0 || !identity || !iterations || !URUI::Interaction::callbackDepth ||
            !URUI::Interaction::animationEvent)
        {
            return -1;
        }
        auto& event = *URUI::Interaction::animationEvent;
        if (!event.GetTargetElement() ||
            event.GetTargetElement()->GetOwnerDocument() != URUI::Elements::Document(document_handle))
        {
            return -1;
        }
        auto key = event.GetParameter<URUI::String>("animation-name", "");
        auto text = event.GetParameter<URUI::String>("animation-id", "0");
        *identity = 0;
        URUI::TypeConverter<URUI::String, uint64_t>::Convert(text, *identity);
        *iterations = event.GetParameter<int>("animation-iterations", 0);
        int required = int(key.size()) + 1;
        if (capacity < required)
        {
            return -required;
        }
        std::memcpy(name, key.c_str(), required);
        return required;
    }
}
