// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8CircularGeofencingRegion.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/modules/v8/V8CircularGeofencingRegionInit.h"
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
const WrapperTypeInfo V8CircularGeofencingRegion::wrapperTypeInfo = { gin::kEmbedderBlink, V8CircularGeofencingRegion::domTemplate, V8CircularGeofencingRegion::refObject, V8CircularGeofencingRegion::derefObject, V8CircularGeofencingRegion::trace, 0, 0, V8CircularGeofencingRegion::preparePrototypeObject, V8CircularGeofencingRegion::installConditionallyEnabledProperties, "CircularGeofencingRegion", &V8GeofencingRegion::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::GarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in CircularGeofencingRegion.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& CircularGeofencingRegion::s_wrapperTypeInfo = V8CircularGeofencingRegion::wrapperTypeInfo;

namespace CircularGeofencingRegionV8Internal {

static void latitudeAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    CircularGeofencingRegion* impl = V8CircularGeofencingRegion::toImpl(holder);
    v8SetReturnValue(info, impl->latitude());
}

static void latitudeAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    CircularGeofencingRegionV8Internal::latitudeAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void longitudeAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    CircularGeofencingRegion* impl = V8CircularGeofencingRegion::toImpl(holder);
    v8SetReturnValue(info, impl->longitude());
}

static void longitudeAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    CircularGeofencingRegionV8Internal::longitudeAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void radiusAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    CircularGeofencingRegion* impl = V8CircularGeofencingRegion::toImpl(holder);
    v8SetReturnValue(info, impl->radius());
}

static void radiusAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    CircularGeofencingRegionV8Internal::radiusAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void constructor(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ConstructionContext, "CircularGeofencingRegion", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    CircularGeofencingRegionInit init;
    {
        if (!isUndefinedOrNull(info[0]) && !info[0]->IsObject()) {
            exceptionState.throwTypeError("parameter 1 ('init') is not an object.");
            exceptionState.throwIfNeeded();
            return;
        }
        V8CircularGeofencingRegionInit::toImpl(info.GetIsolate(), info[0], init, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    RawPtr<CircularGeofencingRegion> impl = CircularGeofencingRegion::create(init);
    v8::Local<v8::Object> wrapper = info.Holder();
    wrapper = impl->associateWithWrapper(info.GetIsolate(), &V8CircularGeofencingRegion::wrapperTypeInfo, wrapper);
    v8SetReturnValue(info, wrapper);
}

} // namespace CircularGeofencingRegionV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8CircularGeofencingRegionAccessors[] = {
    {"latitude", CircularGeofencingRegionV8Internal::latitudeAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"longitude", CircularGeofencingRegionV8Internal::longitudeAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"radius", CircularGeofencingRegionV8Internal::radiusAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

void V8CircularGeofencingRegion::constructorCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SCOPED_SAMPLING_STATE("blink", "DOMConstructor");
    if (!info.IsConstructCall()) {
        V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::constructorNotCallableAsFunction("CircularGeofencingRegion"));
        return;
    }

    if (ConstructorMode::current(info.GetIsolate()) == ConstructorMode::WrapExistingObject) {
        v8SetReturnValue(info, info.Holder());
        return;
    }

    CircularGeofencingRegionV8Internal::constructor(info);
}

static void installV8CircularGeofencingRegionTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    if (!RuntimeEnabledFeatures::geofencingEnabled())
        defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "CircularGeofencingRegion", V8GeofencingRegion::domTemplate(isolate), V8CircularGeofencingRegion::internalFieldCount, 0, 0, 0, 0, 0, 0);
    else
        defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "CircularGeofencingRegion", V8GeofencingRegion::domTemplate(isolate), V8CircularGeofencingRegion::internalFieldCount,
            0, 0,
            V8CircularGeofencingRegionAccessors, WTF_ARRAY_LENGTH(V8CircularGeofencingRegionAccessors),
            0, 0);
    functionTemplate->SetCallHandler(V8CircularGeofencingRegion::constructorCallback);
    functionTemplate->SetLength(1);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
    static const V8DOMConfiguration::ConstantConfiguration V8CircularGeofencingRegionConstants[] = {
        {"MIN_RADIUS", 0, 1.0, 0, V8DOMConfiguration::ConstantTypeDouble},
        {"MAX_RADIUS", 0, 100.0, 0, V8DOMConfiguration::ConstantTypeDouble},
    };
    V8DOMConfiguration::installConstants(isolate, functionTemplate, prototypeTemplate, V8CircularGeofencingRegionConstants, WTF_ARRAY_LENGTH(V8CircularGeofencingRegionConstants));

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8CircularGeofencingRegion::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8CircularGeofencingRegionTemplate);
}

bool V8CircularGeofencingRegion::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8CircularGeofencingRegion::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

CircularGeofencingRegion* V8CircularGeofencingRegion::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8CircularGeofencingRegion::refObject(ScriptWrappable* scriptWrappable)
{
}

void V8CircularGeofencingRegion::derefObject(ScriptWrappable* scriptWrappable)
{
}

} // namespace blink
