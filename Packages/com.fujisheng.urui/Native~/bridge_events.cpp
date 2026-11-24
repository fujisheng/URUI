#include "bridge_runtime.h"
#include "Engine/Source/Core/ElementAnimation.h"
#include <URUI/Core/Elements/ElementFormControl.h>
#include <URUI/Core/StringUtilities.h>
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstring>

namespace URUI::Bridge
{
    static_assert(offsetof(URUIElementEvent, text) == 80 &&
                      sizeof(URUIElementEvent) == (alignof(URUIElementEvent) == 8 ? 88 : 84),
                  "Element event packet ABI");

    namespace
    {
        const char* EventName(int type) noexcept
        {
            static constexpr const char* names[] = {"",
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

        int MapModifiers(int value) noexcept
        {
            int commandShortcuts = 0;
#if defined(__APPLE__)
            if (value & 8)
            {
                commandShortcuts = URUI::Input::KM_CTRL;
            }
#endif
            return ((value & 1) ? URUI::Input::KM_SHIFT : 0) | ((value & 2) ? URUI::Input::KM_CTRL : 0) |
                   ((value & 4) ? URUI::Input::KM_ALT : 0) | ((value & 8) ? URUI::Input::KM_META : 0) |
                   commandShortcuts;
        }

        URUI::Input::KeyIdentifier MapKey(int value) noexcept
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

        void CancelNativeTouch(SurfaceContext& surface, int pointer, PointerState& state)
        {
            if (!state.nativeTouch)
            {
                return;
            }
            state.nativeTouch = false;
            if (!surface.context)
            {
                return;
            }
            surface.context->ProcessMouseLeave();
            surface.context->ProcessTouchCancel(
                URUI::TouchList{{static_cast<URUI::TouchId>(pointer), {state.x, state.y}}});
        }

        bool FocusReceivesInput(const SurfaceContext& surface) noexcept
        {
            auto* focused = surface.context ? surface.context->GetFocusElement() : nullptr;
            return !focused ||
                   (focused->IsVisible(true) && !surface.context->IsDocumentBlockedByModal(focused->GetOwnerDocument()));
        }

        bool SendPointerEvent(SurfaceContext& surface, URUI::Element* target, int type)
        {
            if (!target || !target->GetOwnerDocument() || !target->IsVisible(true) || surface.pendingDestroy)
            {
                return false;
            }
            if (type != 5 && type != 7 && type != 16 &&
                surface.context->IsDocumentBlockedByModal(target->GetOwnerDocument()))
            {
                return false;
            }
            URUI::Dictionary parameters{{"pointer_id", URUI::Variant(surface.currentInput.pointer)},
                                        {"mouse_x", URUI::Variant(surface.currentInput.x)},
                                        {"mouse_y", URUI::Variant(surface.currentInput.y)},
                                        {"button", URUI::Variant(surface.currentInput.button)}};
            return target->DispatchEvent(EventName(type), parameters, true, true);
        }
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
        SurfaceContext* surface;
        URUIDocumentHandle owner;
        URUI::ObserverPtr<URUI::Element> root;
        std::vector<PendingEvent> events;
        int attachedTypes = 0;

        CandidateEvents(SurfaceContext* s, URUIDocumentHandle docOwner) : surface(s), owner(docOwner)
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
                    events.push_back(
                        {event.GetTargetElement()->GetObserverPtr(), event.GetParameters(), surface->currentInput, type});
                    return;
                }
            }
        }
    };

    struct EventSubscription : URUI::EventListener
    {
        SurfaceContext* surface;
        URUI::ObserverPtr<URUI::Element> element;
        uint64_t token;
        URUIDocumentHandle owner;
        int type;
        int capture;
        bool enabled = true;
        bool wholePath = false;

        EventSubscription(SurfaceContext* s, URUI::Element* el, uint64_t id, int event_type, int phase,
                          URUIDocumentHandle docOwner)
            : surface(s), element(el->GetObserverPtr()), token(id), owner(docOwner), type(event_type), capture(phase)
        {
        }

        void ProcessEvent(URUI::Event& event) override
        {
            if (!surface || !enabled || !surface->eventCallback || !element || surface->pendingDestroy ||
                (type == 13 && surface->textHandler.composing))
            {
                return;
            }
            auto* document = ResolveDocument(*surface, owner);
            if (!document || CandidatePending(*surface, document))
            {
                return;
            }
            if (wholePath)
            {
                bool interested = false;
                bool disabled = false;
                const bool skipDisabledCheck =
                    type == 5 || type == 12 || type == 15 || type == 16 || type == 17 || type == 18 || type == 19;
                for (auto* current = event.GetTargetElement(); current; current = current->GetParentNode())
                {
                    interested |= surface->elementReferences.EventInterest(current, type);
                    if (!skipDisabledCheck && !disabled)
                    {
                        disabled = current->HasAttribute("disabled");
                    }
                    // Short-circuit once interested (disabled only matters when !interested).
                    if (interested || current == document)
                    {
                        break;
                    }
                }
                if (!interested)
                {
                    if (disabled && !skipDisabledCheck)
                    {
                        event.StopImmediatePropagation();
                    }
                    return;
                }
            }
            auto payload = surface->currentInput;
            payload.surface = surface->handle;
            payload.document = owner;
            payload.target = surface->elementReferences.Track(event.GetTargetElement(), document);
            payload.current = token;
            payload.type = type;
            payload.phase = static_cast<int>(event.GetPhase()) | (capture ? 8 : 0) | (wholePath ? 16 : 0);
            payload.key = (type == 9 || type == 10) ? surface->currentInput.key : 0;
            // Only extract string parameter for event types that actually carry text/property/value payloads.
            URUI::String text;
            if (type >= 17)
            {
                text = event.GetParameter<URUI::String>("property", "");
            }
            else if (type == 14)
            {
                text = event.GetParameter<URUI::String>("text", "");
            }
            else if (type == 13)
            {
                text = event.GetParameter<URUI::String>("value", "");
            }
            payload.text = text.c_str();
            if (!payload.target)
            {
                return;
            }
            AnimationEventScope animationScope(*surface, event);
            CallbackDepthGuard depth(*surface);
            const int result = surface->eventCallback(&payload);
            if ((result & 2) || surface->pendingDestroy)
            {
                event.StopImmediatePropagation();
            }
            else if (result & 1)
            {
                event.StopPropagation();
            }
        }
    };

    void DestroyEventSubscription(EventSubscription* p) noexcept
    {
        delete p;
    }

    void DestroyCandidateEvents(CandidateEvents* p) noexcept
    {
        delete p;
    }

    namespace
    {
        int ElementListenImpl(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token, int type, int capture,
                              int enabled)
        {
            auto* el = ResolveElement(surface, owner, token);
            if (!el || !EventName(type) || (capture != 0 && capture != 1))
            {
                return URUI_ERR_INVALID_ARG;
            }
            const SubscriptionKey key{token, type, capture};
            auto found = surface.subscriptions.find(key);
            if (found == surface.subscriptions.end())
            {
                if (!enabled)
                {
                    return URUI_OK;
                }
                std::unique_ptr<EventSubscription, EventSubscriptionDeleter> value(
                    new EventSubscription(&surface, el, token, type, capture, owner));
                auto* raw = value.get();
                // Insert into map BEFORE attaching to DOM so OOM never leaves a dangling listener on the element!
                auto [it, inserted] = surface.subscriptions.emplace(key, std::move(value));
                (void)inserted;
                try
                {
                    el->AddEventListener(EventName(type), raw, capture != 0);
                }
                catch (...)
                {
                    surface.subscriptions.erase(it);
                    throw;
                }
            }
            else if (found->second->enabled != (enabled != 0))
            {
                auto& value = *found->second;
                value.enabled = enabled != 0;
                if (enabled)
                {
                    el->AddEventListener(EventName(type), &value, capture != 0);
                }
                else
                {
                    el->RemoveEventListener(EventName(type), &value, capture != 0);
                }
            }
            return URUI_OK;
        }
    }

    void SurfaceTextHandler::OnActivate(URUI::TextInputContext* value)
    {
        active = value;
        composing = false;
        auto* focused = surface && surface->context ? surface->context->GetFocusElement() : nullptr;
        owner = focused ? focused->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
    }

    void SurfaceTextHandler::OnDeactivate(URUI::TextInputContext* value)
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

    void SurfaceTextHandler::OnDestroy(URUI::TextInputContext* value)
    {
        if (active == value)
        {
            active = nullptr;
            composing = false;
            original.clear();
            owner = {};
        }
    }

    std::unique_ptr<CandidateEvents, CandidateEventsDeleter> CreateCandidateEvents(SurfaceContext& surface,
                                                                                   URUIDocumentHandle owner)
    {
        return std::unique_ptr<CandidateEvents, CandidateEventsDeleter>(new CandidateEvents(&surface, owner));
    }

    void AttachCandidateEvents(CandidateEvents& candidate, URUI::ElementDocument& document)
    {
        candidate.Attach(document);
    }

    bool CandidatePending(const SurfaceContext& surface, URUI::Element* root) noexcept
    {
        return std::any_of(surface.pendingDocuments.begin(), surface.pendingDocuments.end(),
                           [root](const auto& candidate) { return candidate->root.get() == root; });
    }

    int DocumentListenImpl(SurfaceContext& surface, URUIDocumentHandle owner)
    {
        auto* state = surface.documentRegistry->Find(owner);
        if (!state)
        {
            return URUI_ERR_NOT_READY;
        }
        state->listening = true;
        auto* document = state->Element();
        if (!document || CandidatePending(surface, document))
        {
            return URUI_OK;
        }
        const auto token = surface.elementReferences.Track(document, document);
        for (int type = 1; EventName(type); ++type)
        {
            const int status = ElementListenImpl(surface, owner, token, type, 1, 1);
            if (status != URUI_OK)
            {
                return status;
            }
            surface.subscriptions.at(SubscriptionKey{token, type, 1})->wholePath = true;
        }
        return URUI_OK;
    }

    void FlushCandidateEvents(SurfaceContext& surface)
    {
        if (surface.callbackDepth > 0 || surface.pendingDestroy || surface.pendingDocuments.empty())
        {
            return;
        }
        decltype(surface.pendingDocuments) ready;
        ready.swap(surface.pendingDocuments);
        for (auto& candidate : ready)
        {
            auto* state = surface.documentRegistry ? surface.documentRegistry->Find(candidate->owner) : nullptr;
            if (!state || state->Element() != candidate->root.get())
            {
                continue;
            }
            candidate->Detach();
            if (!state->listening)
            {
                continue;
            }
            if (DocumentListenImpl(surface, candidate->owner) != URUI_OK)
            {
                throw std::runtime_error("Document event publication failed");
            }
            for (const auto& pending : candidate->events)
            {
                auto* target = pending.target.get();
                auto* document = state->Element();
                if (!target || !surface.elementReferences.Attached(target, document) || surface.pendingDestroy)
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
                InputScopeGuard inputGuard(surface);
                surface.currentInput = pending.input;
                auto deliver = [&](URUI::Element* current, int capture)
                {
                    if (!current || !pending.target || !event.IsImmediatePropagating())
                    {
                        return;
                    }
                    const auto token = surface.elementReferences.Track(current, document);
                    const auto found = surface.subscriptions.find(SubscriptionKey{token, pending.type, capture});
                    if (found != surface.subscriptions.end())
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
            }
        }
    }

    void CancelCompositionFor(SurfaceContext& surface, URUI::Element* element) noexcept
    {
        if (surface.textHandler.composing && surface.textHandler.owner.get() == element)
        {
            urui_text_composition(surface.handle, "", 2);
        }
    }

    void ResetInteraction(SurfaceContext& surface) noexcept
    {
        surface.pendingDocuments.clear();
        for (auto& item : surface.subscriptions)
        {
            auto& value = *item.second;
            if (value.element && value.enabled)
            {
                value.element->RemoveEventListener(EventName(value.type), &value, value.capture != 0);
            }
        }
        surface.subscriptions.clear();
        for (auto& state : surface.pointers)
        {
            CancelNativeTouch(surface, state.id, state);
        }
        surface.pointers.clear();
        surface.primaryTouch = -1;
        surface.textHandler.active = nullptr;
        surface.textHandler.composing = false;
        surface.textHandler.owner = {};
        surface.textHandler.original.clear();
    }

    void CancelDocumentInput(SurfaceContext& surface, URUI::ElementDocument* document) noexcept
    {
        if (!document)
        {
            return;
        }
        try
        {
            // Snapshot pointer IDs on stack (zero heap allocation, OOM-safe) because touch cancellation
            // can erase entries from surface.pointers.
            int ids[64];
            size_t count = 0;
            for (const auto& p : surface.pointers)
            {
                if (count < 64)
                {
                    ids[count++] = p.id;
                }
            }
            for (size_t i = 0; i < count; ++i)
            {
                const int pointer = ids[i];
                auto* state = surface.FindPointer(pointer);
                if (!state)
                {
                    continue;
                }
                auto* target = state->capture ? state->capture.get() : state->pressed.get();
                if (state->gestureDocument.get() == document || (target && target->GetOwnerDocument() == document))
                {
                    urui_pointer_event(surface.handle, pointer, state->kind, 3, state->x, state->y, 0, 0, 0, 0, 0);
                }
                state = surface.FindPointer(pointer);
                if (state && state->hover && state->hover->GetOwnerDocument() == document)
                {
                    auto hovered = state->hover;
                    state->hover = {};
                    try
                    {
                        SendPointerEvent(surface, hovered.get(), 7);
                    }
                    catch (...)
                    {
                    }
                }
            }
            if (surface.textHandler.owner && surface.textHandler.owner->GetOwnerDocument() == document)
            {
                urui_text_composition(surface.handle, "", 2);
            }
        }
        catch (...)
        {
        }
    }

    void CancelBlockedDocumentInput(SurfaceContext& surface) noexcept
    {
        if (!surface.context)
        {
            return;
        }
        for (int i = 0; i < surface.context->GetNumDocuments(); ++i)
        {
            auto* document = surface.context->GetDocument(i);
            if (surface.context->IsDocumentBlockedByModal(document))
            {
                CancelDocumentInput(surface, document);
            }
        }
    }

    void RetireDocumentInteraction(SurfaceContext& surface, URUIDocumentHandle owner,
                                   URUI::ElementDocument* document) noexcept
    {
        if (document)
        {
            CancelDocumentInput(surface, document);
            if (auto resources = document->GetResourceContext())
            {
                resources->Retire();
            }
            surface.pendingDocuments.erase(std::remove_if(surface.pendingDocuments.begin(),
                                                          surface.pendingDocuments.end(),
                                                          [document](const auto& candidate)
                                                          { return candidate->root.get() == document; }),
                                           surface.pendingDocuments.end());
        }
        for (auto it = surface.subscriptions.begin(); it != surface.subscriptions.end();)
        {
            auto& value = *it->second;
            if (value.owner != owner && (!document || !value.element || value.element->GetOwnerDocument() != document))
            {
                ++it;
                continue;
            }
            if (value.element && value.enabled)
            {
                value.element->RemoveEventListener(EventName(value.type), &value, value.capture != 0);
            }
            it = surface.subscriptions.erase(it);
        }
    }

    void PruneDeadSubscriptions(SurfaceContext& surface) noexcept
    {
        if (surface.callbackDepth != 0 || surface.subscriptions.empty())
        {
            return;
        }
        for (auto it = surface.subscriptions.begin(); it != surface.subscriptions.end();)
        {
            if (!it->second || !it->second->element || !it->second->enabled)
            {
                it = surface.subscriptions.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_animation_api_version(void) { return 2; }

    uint64_t urui_animation_next_identity(void)
    {
        return URUI::AnimationIdentityCounter();
    }

    int urui_animation_current_event(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, char* name,
                                     int capacity, int* required, uint64_t* identity, int* iterations)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!required || !identity || !iterations || capacity < 0 || (!name && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        *identity = 0;
        *iterations = 0;
        if (surface->callbackDepth <= 0 || !surface->animationEvent)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& event = *surface->animationEvent;
        if (!event.GetTargetElement() || event.GetTargetElement()->GetOwnerDocument() != ResolveDocument(*surface, owner))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto key = event.GetParameter<URUI::String>("animation-name", "");
        const auto text = event.GetParameter<URUI::String>("animation-id", "0");
        URUI::TypeConverter<URUI::String, uint64_t>::Convert(text, *identity);
        *iterations = event.GetParameter<int>("animation-iterations", 0);
        return EngineRuntime::CopyString(key, name, capacity, required);
    }

    int urui_set_event_callback(URUISurfaceHandle surfaceHandle, URUIElementCallback value)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        surface->eventCallback = value;
        return URUI_OK;
    }

    int urui_element_event_interest(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                    uint32_t mask, uint64_t version)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!ResolveElement(*surface, owner, token))
        {
            return URUI_ERR_NOT_READY;
        }
        return surface->elementReferences.SetEventInterest(token, mask, version);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_document_listen(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        return DocumentListenImpl(*surface, owner);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_listen(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int type,
                            int capture, int enabled)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        return ElementListenImpl(*surface, owner, token, type, capture, enabled);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_pointer_capture(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, int pointer, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = token ? ResolveElement(*surface, owner, token) : nullptr;
        if (token && (!el || !el->IsVisible(true) || surface->context->IsDocumentBlockedByModal(document) ||
                      (el != document && !document->Contains(el))))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* state = surface->FindPointer(pointer);
        if (!state)
        {
            return URUI_ERR_NOT_READY;
        }
        if (token && !state->active)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* origin = state->capture ? state->capture->GetOwnerDocument() : state->gestureDocument.get();
        if (origin != document)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        auto old = state->capture;
        state->capture = el ? el->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        if (old && old.get() != el)
        {
            SendPointerEvent(*surface, old.get(), 16);
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_pointer_captured(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, int pointer)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* state = surface->FindPointer(pointer);
        return !state ? 0 : surface->elementReferences.Track(state->capture.get(), document);
    }
    catch (...)
    {
        return 0;
    }

    int urui_pointer_poll(URUISurfaceHandle surfaceHandle, int pointer, int kind, float x, float y, int buttons,
                          int modifiers)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (pointer < 0 || kind < 0 || kind > 1 || buttons < 0 || buttons > 7 || modifiers < 0 || modifiers > 15 ||
            !std::isfinite(x) || !std::isfinite(y))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto revision = surface->context->GetHitTestRevision();
        URUI::ObserverPtr<URUI::Element> dispatchedCapture;
        if (const auto* existing = surface->FindPointer(pointer))
        {
            if (existing->pollReady && revision != UINT64_MAX && existing->pollRevision == revision &&
                existing->x == x && existing->y == y && existing->kind == kind && existing->buttons == buttons &&
                existing->pollModifiers == modifiers && existing->capture.get() == existing->pollCapture.get())
            {
                return URUI_OK;
            }
            dispatchedCapture = existing->capture;
        }
        const auto result = urui_pointer_event(surfaceHandle, pointer, kind, 1, x, y, 0, buttons, 0, 0, modifiers);
        if (result == URUI_OK && surface->context && !surface->pendingDestroy)
        {
            if (auto* current = surface->FindPointer(pointer))
            {
                current->pollReady = true;
                current->pollRevision = revision;
                current->pollModifiers = modifiers;
                current->pollCapture = dispatchedCapture;
            }
        }
        return result;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_pointer_event(URUISurfaceHandle surfaceHandle, int pointer, int kind, int phase, float x, float y,
                           int button, int buttons, float dx, float dy, int modifiers)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (phase < 0 || phase > 4 || kind < 0 || kind > 1 || pointer < 0 || button < 0 || button > 2 ||
            !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(dx) || !std::isfinite(dy))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        InputScopeGuard inputGuard(*surface);
        surface->currentInput = {};
        surface->currentInput.surface = surface->handle;
        surface->currentInput.pointer = pointer;
        surface->currentInput.kind = kind;
        surface->currentInput.button = button;
        surface->currentInput.buttons = buttons;
        surface->currentInput.x = x;
        surface->currentInput.y = y;
        surface->currentInput.dx = dx;
        surface->currentInput.dy = dy;
        surface->currentInput.modifiers = modifiers;

        auto& stateRef = surface->GetOrCreatePointer(pointer);
        stateRef.pollReady = false;
        if (stateRef.nativeTouch && (kind != 1 || phase == 0))
        {
            CancelNativeTouch(*surface, pointer, stateRef);
        }
        if (surface->pendingDestroy)
        {
            return URUI_OK;
        }
        auto* state = surface->FindPointer(pointer);
        if (!state)
        {
            state = &surface->GetOrCreatePointer(pointer);
        }
        state->x = x;
        state->y = y;
        state->kind = kind;
        state->buttons = buttons;
        auto* hit = surface->context->GetElementAtPoint({x, y});
        if (hit == surface->context->GetRootElement())
        {
            hit = nullptr;
        }
        auto* target = state->capture ? state->capture.get() : phase == 3 ? state->pressed.get() : hit;
        auto heldTarget = target ? target->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        if (phase == 0)
        {
            state->active = true;
            state->pressed = heldTarget;
            auto* ownerDoc = target ? target->GetOwnerDocument() : nullptr;
            state->gestureDocument = ownerDoc ? ownerDoc->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
        }
        else if (phase == 2 || phase == 3)
        {
            state->active = false;
        }
        if (state->hover.get() != hit && !state->capture)
        {
            auto old = state->hover;
            state->hover = hit ? hit->GetObserverPtr() : URUI::ObserverPtr<URUI::Element>{};
            if (old)
            {
                SendPointerEvent(*surface, old.get(), 7);
            }
            if (auto* st = surface->FindPointer(pointer); st && st->hover)
            {
                SendPointerEvent(*surface, st->hover.get(), 6);
            }
        }
        if (surface->pendingDestroy)
        {
            return URUI_OK;
        }
        const bool allowed = !heldTarget || SendPointerEvent(*surface, heldTarget.get(),
                                                             phase == 4   ? 8
                                                             : phase == 0 ? 2
                                                             : phase == 1 ? 3
                                                             : phase == 2 ? 4
                                                                          : 5);
        if (surface->pendingDestroy)
        {
            return URUI_OK;
        }
        state = surface->FindPointer(pointer);
        if (!state)
        {
            return URUI_OK;
        }
        if (phase == 0 && kind == 1 && surface->primaryTouch < 0)
        {
            surface->primaryTouch = pointer;
        }
        const bool primary = kind == 0 || surface->primaryTouch == pointer;
        if (allowed && primary)
        {
            if (kind == 1 && phase != 4)
            {
                const URUI::Touch touch{static_cast<URUI::TouchId>(pointer), {x, y}};
                if (phase == 0)
                {
                    state->nativeTouch = true;
                    surface->context->ProcessTouchStart(URUI::TouchList{touch}, MapModifiers(modifiers));
                }
                else if (phase == 1 && state->nativeTouch)
                {
                    surface->context->ProcessTouchMove(URUI::TouchList{touch}, MapModifiers(modifiers));
                }
                else if (phase == 2 && state->nativeTouch)
                {
                    state->nativeTouch = false;
                    surface->context->ProcessTouchEnd(URUI::TouchList{touch}, MapModifiers(modifiers));
                }
                else if (phase == 3)
                {
                    CancelNativeTouch(*surface, pointer, *state);
                }
            }
            else
            {
                surface->context->ProcessMouseMove(static_cast<int>(x), static_cast<int>(y), MapModifiers(modifiers));
                if (phase == 0)
                {
                    surface->context->ProcessMouseButtonDown(button, MapModifiers(modifiers));
                }
                else if (phase == 2)
                {
                    surface->context->ProcessMouseButtonUp(button, MapModifiers(modifiers));
                }
                else if (phase == 3)
                {
                    surface->context->ProcessMouseLeave();
                    surface->context->ProcessMouseButtonUp(button, MapModifiers(modifiers));
                }
                else if (phase == 4)
                {
                    surface->context->ProcessMouseWheel(URUI::Vector2f(dx, dy), MapModifiers(modifiers));
                }
            }
        }
        else if (allowed && phase == 2 && heldTarget && state->pressed.get() == hit)
        {
            SendPointerEvent(*surface, heldTarget.get(), 1);
        }
        else if (primary && (phase == 2 || phase == 3))
        {
            if (state->nativeTouch)
            {
                CancelNativeTouch(*surface, pointer, *state);
            }
            else
            {
                surface->context->ProcessMouseLeave();
                surface->context->ProcessMouseButtonUp(button, MapModifiers(modifiers));
            }
        }
        if (phase == 2 || phase == 3)
        {
            if (auto* st = surface->FindPointer(pointer))
            {
                st->pressed = {};
                auto captured = st->capture;
                st->capture = {};
                if (captured)
                {
                    SendPointerEvent(*surface, captured.get(), 16);
                }
                if (auto* st2 = surface->FindPointer(pointer))
                {
                    st2->gestureDocument = {};
                }
            }
            if (kind == 1)
            {
                surface->ErasePointer(pointer);
            }
            if (surface->primaryTouch == pointer)
            {
                surface->primaryTouch = -1;
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_input_reset(URUISurfaceHandle surfaceHandle)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        auto current = surface->pointers;
        for (const auto& st : current)
        {
            urui_pointer_event(surfaceHandle, st.id, st.kind, 3, st.x, st.y, 0, 0, 0, 0, 0);
        }
        surface->pointers.clear();
        surface->context->ProcessMouseLeave();
        for (int button = 0; button < 3; ++button)
        {
            surface->context->ProcessMouseButtonUp(button, 0);
        }
        auto* focused = surface->context->GetFocusElement();
        if (auto* owner = focused ? focused->GetOwnerDocument() : nullptr)
        {
            owner->Focus();
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_cancel_pointers(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* targetOwner = ResolveElement(*surface, owner, token);
        if (!targetOwner)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        auto weakOwner = targetOwner->GetObserverPtr();
        auto current = surface->pointers;
        for (const auto& st : current)
        {
            auto* root = weakOwner.get();
            if (!root)
            {
                break;
            }
            auto* target = st.capture ? st.capture.get() : st.pressed.get();
            if (target && (target == root || root->Contains(target)))
            {
                urui_pointer_event(surfaceHandle, st.id, st.kind, 3, st.x, st.y, 0, 0, 0, 0, 0);
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_key_event(URUISurfaceHandle surfaceHandle, int key, int pressed, int modifiers)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!FocusReceivesInput(*surface))
        {
            return URUI_ABSENT;
        }
        const auto mapped = MapKey(key);
        if (mapped == URUI::Input::KI_UNKNOWN)
        {
            return URUI_ABSENT;
        }
        ActiveSurfaceScope scope(surface);
        InputScopeGuard inputGuard(*surface);
        surface->currentInput = {};
        surface->currentInput.surface = surface->handle;
        surface->currentInput.pointer = -1;
        surface->currentInput.key = key;
        surface->currentInput.modifiers = modifiers;
        if (pressed)
        {
            surface->context->ProcessKeyDown(mapped, MapModifiers(modifiers));
        }
        else
        {
            surface->context->ProcessKeyUp(mapped, MapModifiers(modifiers));
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_text_input(URUISurfaceHandle surfaceHandle, const char* text)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!text)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (!FocusReceivesInput(*surface))
        {
            return URUI_ABSENT;
        }
        ActiveSurfaceScope scope(surface);
        surface->context->ProcessTextInput(URUI::String(text));
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_text_composition(URUISurfaceHandle surfaceHandle, const char* text, int operation)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!text || operation < 0 || operation > 2)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto& handler = surface->textHandler;
        if (!handler.active)
        {
            return URUI_ERR_NOT_READY;
        }
        if (operation != 2 && !FocusReceivesInput(*surface))
        {
            return URUI_ABSENT;
        }
        ActiveSurfaceScope scope(surface);
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(surface->context->GetFocusElement());
        if (!control)
        {
            return URUI_ERR_NOT_READY;
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
            return URUI_OK;
        }
        if (!handler.composing)
        {
            if (operation == 1)
            {
                surface->context->ProcessTextInput(URUI::String(text));
                return URUI_OK;
            }
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
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_text_selection(URUISurfaceHandle surfaceHandle, int* start, int* end, int write)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!start || !end)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (!surface->textHandler.active)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (write)
        {
            if (*start < 0 || *end < *start)
            {
                return URUI_ERR_INVALID_ARG;
            }
            surface->textHandler.active->SetSelectionRange(*start, *end);
        }
        else
        {
            surface->textHandler.active->GetSelectionRange(*start, *end);
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_clipboard_version(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->clipboardVersion;
    }

    int urui_clipboard_write(URUISurfaceHandle surfaceHandle, const char* text)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!text)
        {
            return URUI_ERR_INVALID_ARG;
        }
        surface->clipboard = text;
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_clipboard_read(URUISurfaceHandle surfaceHandle, char* out, int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        return EngineRuntime::CopyString(surface->clipboard, out, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_process_deferred_operations(URUISurfaceHandle surfaceHandle)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (surface->pendingDestroy)
        {
            surface->pendingDestroy = false;
            return DestroySurfaceInternal(surfaceHandle, true);
        }
        ActiveSurfaceScope scope(surface);
        FlushCandidateEvents(*surface);
        for (auto it = surface->subscriptions.begin(); it != surface->subscriptions.end();)
        {
            if (!it->second->element || !it->second->enabled)
            {
                it = surface->subscriptions.erase(it);
            }
            else
            {
                ++it;
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }
}
