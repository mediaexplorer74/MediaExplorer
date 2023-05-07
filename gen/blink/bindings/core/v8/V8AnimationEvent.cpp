// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8AnimationEvent.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8AnimationEventInit.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
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
const WrapperTypeInfo V8AnimationEvent::wrapperTypeInfo = { gin::kEmbedderBlink, V8AnimationEvent::domTemplate, V8AnimationEvent::refObject, V8AnimationEvent::derefObject, V8AnimationEvent::trace, 0, 0, V8AnimationEvent::preparePrototypeObject, V8AnimationEvent::installConditionallyEnabledProperties, "AnimationEvent", &V8Event::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in AnimationEvent.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& AnimationEvent::s_wrapperTypeInfo = V8AnimationEvent::wrapperTypeInfo;

namespace AnimationEventV8Internal {

static void animationNameAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    AnimationEvent* impl = V8AnimationEvent::toImpl(holder);
    v8SetReturnValueString(info, impl->animationName(), info.GetIsolate());
}

static void animationNameAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    AnimationEventV8Internal::animationNameAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void elapsedTimeAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    AnimationEvent* impl = V8AnimationEvent::toImpl(holder);
    v8SetReturnValue(info, impl->elapsedTime());
}

static void elapsedTimeAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    AnimationEventV8Internal::elapsedTimeAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void constructor(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ConstructionContext, "AnimationEvent", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    V8StringResource<> type;
    AnimationEventInit eventInitDict;
    {
        type = info[0];
        if (!type.prepare())
            return;
        if (!isUndefinedOrNull(info[1]) && !info[1]->IsObject()) {
            exceptionState.throwTypeError("parameter 2 ('eventInitDict') is not an object.");
            exceptionState.throwIfNeeded();
            return;
        }
        V8AnimationEventInit::toImpl(info.GetIsolate(), info[1], eventInitDict, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    RefPtrWillBeRawPtr<AnimationEvent> impl = AnimationEvent::create(type, eventInitDict);
    v8::Local<v8::Object> wrapper = info.Holder();
    wrapper = impl->associateWithWrapper(info.GetIsolate(), &V8AnimationEvent::wrapperTypeInfo, wrapper);
    v8SetReturnValue(info, wrapper);
}

} // namespace AnimationEventV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8AnimationEventAccessors[] = {
    {"animationName", AnimationEventV8Internal::animationNameAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"elapsedTime", AnimationEventV8Internal::elapsedTimeAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

void V8AnimationEvent::constructorCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SCOPED_SAMPLING_STATE("blink", "DOMConstructor");
    if (!info.IsConstructCall()) {
        V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::constructorNotCallableAsFunction("AnimationEvent"));
        return;
    }

    if (ConstructorMode::current(info.GetIsolate()) == ConstructorMode::WrapExistingObject) {
        v8SetReturnValue(info, info.Holder());
        return;
    }

    AnimationEventV8Internal::constructor(info);
}

static void installV8AnimationEventTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "AnimationEvent", V8Event::domTemplate(isolate), V8AnimationEvent::internalFieldCount,
        0, 0,
        V8AnimationEventAccessors, WTF_ARRAY_LENGTH(V8AnimationEventAccessors),
        0, 0);
    functionTemplate->SetCallHandler(V8AnimationEvent::constructorCallback);
    functionTemplate->SetLength(1);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8AnimationEvent::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8AnimationEventTemplate);
}

bool V8AnimationEvent::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8AnimationEvent::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

AnimationEvent* V8AnimationEvent::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8AnimationEvent::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<AnimationEvent>()->ref();
#endif
}

void V8AnimationEvent::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<AnimationEvent>()->deref();
#endif
}

} // namespace blink
