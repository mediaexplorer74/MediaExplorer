// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8EventTarget.h"

#include "bindings/core/v8/BindingSecurity.h"
#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8Event.h"
#include "bindings/core/v8/V8EventListenerList.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "core/frame/LocalDOMWindow.h"
#include "platform/RuntimeEnabledFeatures.h"
#include "platform/TraceEvent.h"
#include "wtf/GetPtr.h"
#include "wtf/RefPtr.h"

namespace blink {

// Suppress warning: global constructors, because struct WrapperTypeInfo is trivial
// and does not depend on another global objects.
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wglobal-constructors"
#endif
const WrapperTypeInfo V8EventTarget::wrapperTypeInfo = { gin::kEmbedderBlink, V8EventTarget::domTemplate, V8EventTarget::refObject, V8EventTarget::derefObject, V8EventTarget::trace, 0, 0, V8EventTarget::preparePrototypeObject, V8EventTarget::installConditionallyEnabledProperties, "EventTarget", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::InheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in EventTarget.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& EventTarget::s_wrapperTypeInfo = V8EventTarget::wrapperTypeInfo;

namespace EventTargetV8Internal {

static void addEventListenerMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "addEventListener", "EventTarget", info.Holder(), info.GetIsolate());
    EventTarget* impl = V8EventTarget::toImpl(info.Holder());
    if (LocalDOMWindow* window = impl->toDOMWindow()) {
        if (!BindingSecurity::shouldAllowAccessToFrame(info.GetIsolate(), window->frame(), exceptionState)) {
            exceptionState.throwIfNeeded();
            return;
        }
        if (!window->document())
            return;
    }
    V8StringResource<TreatNullAsNullString> type;
    RefPtr<EventListener> listener;
    bool capture;
    {
        if (!info[0]->IsUndefined()) {
            type = info[0];
            if (!type.prepare())
                return;
        } else {
            type = nullptr;
        }
        if (!info[1]->IsUndefined()) {
            listener = V8EventListenerList::getEventListener(ScriptState::current(info.GetIsolate()), info[1], false, ListenerFindOrCreate);
        } else {
            listener = nullptr;
        }
        if (!info[2]->IsUndefined()) {
            capture = toBoolean(info.GetIsolate(), info[2], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        } else {
            capture = false;
        }
    }
    V8EventTarget::addEventListenerMethodPrologueCustom(info, impl);
    impl->addEventListener(type, listener, capture);
    V8EventTarget::addEventListenerMethodEpilogueCustom(info, impl);
}

static void addEventListenerMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventTargetV8Internal::addEventListenerMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void removeEventListenerMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "removeEventListener", "EventTarget", info.Holder(), info.GetIsolate());
    EventTarget* impl = V8EventTarget::toImpl(info.Holder());
    if (LocalDOMWindow* window = impl->toDOMWindow()) {
        if (!BindingSecurity::shouldAllowAccessToFrame(info.GetIsolate(), window->frame(), exceptionState)) {
            exceptionState.throwIfNeeded();
            return;
        }
        if (!window->document())
            return;
    }
    V8StringResource<TreatNullAsNullString> type;
    RefPtr<EventListener> listener;
    bool capture;
    {
        if (!info[0]->IsUndefined()) {
            type = info[0];
            if (!type.prepare())
                return;
        } else {
            type = nullptr;
        }
        if (!info[1]->IsUndefined()) {
            listener = V8EventListenerList::getEventListener(ScriptState::current(info.GetIsolate()), info[1], false, ListenerFindOnly);
        } else {
            listener = nullptr;
        }
        if (!info[2]->IsUndefined()) {
            capture = toBoolean(info.GetIsolate(), info[2], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        } else {
            capture = false;
        }
    }
    V8EventTarget::removeEventListenerMethodPrologueCustom(info, impl);
    impl->removeEventListener(type, listener, capture);
    V8EventTarget::removeEventListenerMethodEpilogueCustom(info, impl);
}

static void removeEventListenerMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventTargetV8Internal::removeEventListenerMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void dispatchEventMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "dispatchEvent", "EventTarget", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    EventTarget* impl = V8EventTarget::toImpl(info.Holder());
    if (LocalDOMWindow* window = impl->toDOMWindow()) {
        if (!BindingSecurity::shouldAllowAccessToFrame(info.GetIsolate(), window->frame(), exceptionState)) {
            exceptionState.throwIfNeeded();
            return;
        }
        if (!window->document())
            return;
    }
    Event* event;
    {
        event = V8Event::toImplWithTypeCheck(info.GetIsolate(), info[0]);
    }
    bool result = impl->dispatchEvent(event, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValueBool(info, result);
}

static void dispatchEventMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventTargetV8Internal::dispatchEventMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace EventTargetV8Internal

static const V8DOMConfiguration::MethodConfiguration V8EventTargetMethods[] = {
    {"addEventListener", EventTargetV8Internal::addEventListenerMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"removeEventListener", EventTargetV8Internal::removeEventListenerMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"dispatchEvent", EventTargetV8Internal::dispatchEventMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8EventTargetTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "EventTarget", v8::Local<v8::FunctionTemplate>(), V8EventTarget::internalFieldCount,
        0, 0,
        0, 0,
        V8EventTargetMethods, WTF_ARRAY_LENGTH(V8EventTargetMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8EventTarget::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8EventTargetTemplate);
}

bool V8EventTarget::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8EventTarget::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

EventTarget* V8EventTarget::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8EventTarget::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<EventTarget>()->ref();
#endif
}

void V8EventTarget::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<EventTarget>()->deref();
#endif
}

#ifdef MINIBLINK_NOT_IMPLEMENTED
v8::Local<v8::Value> toV8(EventTarget*, v8::Local<class v8::Object>, v8::Isolate *)
{
    notImplemented();
    return v8::Local<v8::Value>();
}

v8::Local<v8::Value> toV8(EventTarget& impl, v8::Local<v8::Object> creationContext, v8::Isolate* isolate)
{
    v8::Local<v8::Object> v8Object = v8::Object::New(isolate);

    if (!toV8EventTarget(impl, v8Object, creationContext, isolate))
        return v8::Local<v8::Value>();

    return v8Object;
}
#endif // MINIBLINK_NOT_IMPLEMENTED

} // namespace blink
