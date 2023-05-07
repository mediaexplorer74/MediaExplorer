// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8TextEvent.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/core/v8/V8Window.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "core/frame/UseCounter.h"
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
const WrapperTypeInfo V8TextEvent::wrapperTypeInfo = { gin::kEmbedderBlink, V8TextEvent::domTemplate, V8TextEvent::refObject, V8TextEvent::derefObject, V8TextEvent::trace, 0, 0, V8TextEvent::preparePrototypeObject, V8TextEvent::installConditionallyEnabledProperties, "TextEvent", &V8UIEvent::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in TextEvent.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& TextEvent::s_wrapperTypeInfo = V8TextEvent::wrapperTypeInfo;

namespace TextEventV8Internal {

static void dataAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    TextEvent* impl = V8TextEvent::toImpl(holder);
    v8SetReturnValueString(info, impl->data(), info.GetIsolate());
}

static void dataAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::V8TextEvent_Data_AttributeGetter);
    TextEventV8Internal::dataAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void initTextEventMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "initTextEvent", "TextEvent", info.Holder(), info.GetIsolate());
    TextEvent* impl = V8TextEvent::toImpl(info.Holder());
    V8StringResource<> typeArg;
    bool canBubbleArg;
    bool cancelableArg;
    DOMWindow* viewArg;
    V8StringResource<> dataArg;
    {
        typeArg = info[0];
        if (!typeArg.prepare())
            return;
        canBubbleArg = toBoolean(info.GetIsolate(), info[1], exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
        cancelableArg = toBoolean(info.GetIsolate(), info[2], exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
        viewArg = toDOMWindow(info.GetIsolate(), info[3]);
        dataArg = info[4];
        if (!dataArg.prepare())
            return;
    }
    impl->initTextEvent(typeArg, canBubbleArg, cancelableArg, viewArg, dataArg);
}

static void initTextEventMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::V8TextEvent_InitTextEvent_Method);
    TextEventV8Internal::initTextEventMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace TextEventV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8TextEventAccessors[] = {
    {"data", TextEventV8Internal::dataAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static const V8DOMConfiguration::MethodConfiguration V8TextEventMethods[] = {
    {"initTextEvent", TextEventV8Internal::initTextEventMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8TextEventTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "TextEvent", V8UIEvent::domTemplate(isolate), V8TextEvent::internalFieldCount,
        0, 0,
        V8TextEventAccessors, WTF_ARRAY_LENGTH(V8TextEventAccessors),
        V8TextEventMethods, WTF_ARRAY_LENGTH(V8TextEventMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8TextEvent::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8TextEventTemplate);
}

bool V8TextEvent::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8TextEvent::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

TextEvent* V8TextEvent::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8TextEvent::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<TextEvent>()->ref();
#endif
}

void V8TextEvent::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<TextEvent>()->deref();
#endif
}

} // namespace blink
