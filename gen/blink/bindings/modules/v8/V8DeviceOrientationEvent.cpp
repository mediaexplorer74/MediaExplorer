// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8DeviceOrientationEvent.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
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
const WrapperTypeInfo V8DeviceOrientationEvent::wrapperTypeInfo = { gin::kEmbedderBlink, V8DeviceOrientationEvent::domTemplate, V8DeviceOrientationEvent::refObject, V8DeviceOrientationEvent::derefObject, V8DeviceOrientationEvent::trace, 0, 0, V8DeviceOrientationEvent::preparePrototypeObject, V8DeviceOrientationEvent::installConditionallyEnabledProperties, "DeviceOrientationEvent", &V8Event::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in DeviceOrientationEvent.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& DeviceOrientationEvent::s_wrapperTypeInfo = V8DeviceOrientationEvent::wrapperTypeInfo;

namespace DeviceOrientationEventV8Internal {

static void alphaAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DeviceOrientationEvent* impl = V8DeviceOrientationEvent::toImpl(holder);
    bool isNull = false;
    double cppValue(impl->alpha(isNull));
    if (isNull) {
        v8SetReturnValueNull(info);
        return;
    }
    v8SetReturnValue(info, cppValue);
}

static void alphaAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DeviceOrientationEventV8Internal::alphaAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void betaAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DeviceOrientationEvent* impl = V8DeviceOrientationEvent::toImpl(holder);
    bool isNull = false;
    double cppValue(impl->beta(isNull));
    if (isNull) {
        v8SetReturnValueNull(info);
        return;
    }
    v8SetReturnValue(info, cppValue);
}

static void betaAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DeviceOrientationEventV8Internal::betaAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void gammaAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DeviceOrientationEvent* impl = V8DeviceOrientationEvent::toImpl(holder);
    bool isNull = false;
    double cppValue(impl->gamma(isNull));
    if (isNull) {
        v8SetReturnValueNull(info);
        return;
    }
    v8SetReturnValue(info, cppValue);
}

static void gammaAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DeviceOrientationEventV8Internal::gammaAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void absoluteAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DeviceOrientationEvent* impl = V8DeviceOrientationEvent::toImpl(holder);
    bool isNull = false;
    bool cppValue(impl->absolute(isNull));
    if (isNull) {
        v8SetReturnValueNull(info);
        return;
    }
    v8SetReturnValueBool(info, cppValue);
}

static void absoluteAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DeviceOrientationEventV8Internal::absoluteAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void initDeviceOrientationEventMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "initDeviceOrientationEvent", "DeviceOrientationEvent", info.Holder(), info.GetIsolate());
    DeviceOrientationEvent* impl = V8DeviceOrientationEvent::toImpl(info.Holder());
    V8StringResource<> type;
    bool bubbles;
    bool cancelable;
    Nullable<double> alpha;
    Nullable<double> beta;
    Nullable<double> gamma;
    Nullable<bool> absolute;
    {
        type = info[0];
        if (!type.prepare())
            return;
        bubbles = toBoolean(info.GetIsolate(), info[1], exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
        cancelable = toBoolean(info.GetIsolate(), info[2], exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
        if (!isUndefinedOrNull(info[3])) {
            alpha = toRestrictedDouble(info.GetIsolate(), info[3], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        }
        if (!isUndefinedOrNull(info[4])) {
            beta = toRestrictedDouble(info.GetIsolate(), info[4], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        }
        if (!isUndefinedOrNull(info[5])) {
            gamma = toRestrictedDouble(info.GetIsolate(), info[5], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        }
        if (!isUndefinedOrNull(info[6])) {
            absolute = toBoolean(info.GetIsolate(), info[6], exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        }
    }
    impl->initDeviceOrientationEvent(type, bubbles, cancelable, alpha, beta, gamma, absolute);
}

static void initDeviceOrientationEventMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::V8DeviceOrientationEvent_InitDeviceOrientationEvent_Method);
    DeviceOrientationEventV8Internal::initDeviceOrientationEventMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace DeviceOrientationEventV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8DeviceOrientationEventAccessors[] = {
    {"alpha", DeviceOrientationEventV8Internal::alphaAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"beta", DeviceOrientationEventV8Internal::betaAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"gamma", DeviceOrientationEventV8Internal::gammaAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"absolute", DeviceOrientationEventV8Internal::absoluteAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static const V8DOMConfiguration::MethodConfiguration V8DeviceOrientationEventMethods[] = {
    {"initDeviceOrientationEvent", DeviceOrientationEventV8Internal::initDeviceOrientationEventMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8DeviceOrientationEventTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "DeviceOrientationEvent", V8Event::domTemplate(isolate), V8DeviceOrientationEvent::internalFieldCount,
        0, 0,
        V8DeviceOrientationEventAccessors, WTF_ARRAY_LENGTH(V8DeviceOrientationEventAccessors),
        V8DeviceOrientationEventMethods, WTF_ARRAY_LENGTH(V8DeviceOrientationEventMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8DeviceOrientationEvent::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8DeviceOrientationEventTemplate);
}

bool V8DeviceOrientationEvent::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8DeviceOrientationEvent::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

DeviceOrientationEvent* V8DeviceOrientationEvent::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8DeviceOrientationEvent::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<DeviceOrientationEvent>()->ref();
#endif
}

void V8DeviceOrientationEvent::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<DeviceOrientationEvent>()->deref();
#endif
}

} // namespace blink
