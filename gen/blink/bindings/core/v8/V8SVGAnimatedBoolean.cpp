// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8SVGAnimatedBoolean.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/core/v8/V8SVGElement.h"
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
const WrapperTypeInfo V8SVGAnimatedBoolean::wrapperTypeInfo = { gin::kEmbedderBlink, V8SVGAnimatedBoolean::domTemplate, V8SVGAnimatedBoolean::refObject, V8SVGAnimatedBoolean::derefObject, V8SVGAnimatedBoolean::trace, 0, V8SVGAnimatedBoolean::visitDOMWrapper, V8SVGAnimatedBoolean::preparePrototypeObject, V8SVGAnimatedBoolean::installConditionallyEnabledProperties, "SVGAnimatedBoolean", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Dependent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in SVGAnimatedBoolean.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& SVGAnimatedBoolean::s_wrapperTypeInfo = V8SVGAnimatedBoolean::wrapperTypeInfo;

namespace SVGAnimatedBooleanV8Internal {

static void baseValAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    SVGAnimatedBoolean* impl = V8SVGAnimatedBoolean::toImpl(holder);
    v8SetReturnValueBool(info, impl->baseVal());
}

static void baseValAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    SVGAnimatedBooleanV8Internal::baseValAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void baseValAttributeSetter(v8::Local<v8::Value> v8Value, const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    ExceptionState exceptionState(ExceptionState::SetterContext, "baseVal", "SVGAnimatedBoolean", holder, info.GetIsolate());
    SVGAnimatedBoolean* impl = V8SVGAnimatedBoolean::toImpl(holder);
    bool cppValue = toBoolean(info.GetIsolate(), v8Value, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    impl->setBaseVal(cppValue, exceptionState);
    exceptionState.throwIfNeeded();
}

static void baseValAttributeSetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Value> v8Value = info[0];
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMSetter");
    SVGAnimatedBooleanV8Internal::baseValAttributeSetter(v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void animValAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    SVGAnimatedBoolean* impl = V8SVGAnimatedBoolean::toImpl(holder);
    v8SetReturnValueBool(info, impl->animVal());
}

static void animValAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    SVGAnimatedBooleanV8Internal::animValAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace SVGAnimatedBooleanV8Internal

void V8SVGAnimatedBoolean::visitDOMWrapper(v8::Isolate* isolate, ScriptWrappable* scriptWrappable, const v8::Persistent<v8::Object>& wrapper)
{
    SVGAnimatedBoolean* impl = scriptWrappable->toImpl<SVGAnimatedBoolean>();
    v8::Local<v8::Object> creationContext = v8::Local<v8::Object>::New(isolate, wrapper);
    V8WrapperInstantiationScope scope(creationContext, isolate);
    SVGElement* contextElement = impl->contextElement();
    if (contextElement) {
        if (DOMDataStore::containsWrapper(contextElement, isolate))
            DOMDataStore::setWrapperReference(wrapper, contextElement, isolate);
    }
}

static const V8DOMConfiguration::AccessorConfiguration V8SVGAnimatedBooleanAccessors[] = {
    {"baseVal", SVGAnimatedBooleanV8Internal::baseValAttributeGetterCallback, SVGAnimatedBooleanV8Internal::baseValAttributeSetterCallback, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"animVal", SVGAnimatedBooleanV8Internal::animValAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static void installV8SVGAnimatedBooleanTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    if (!RuntimeEnabledFeatures::svg1DOMEnabled())
        defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "SVGAnimatedBoolean", v8::Local<v8::FunctionTemplate>(), V8SVGAnimatedBoolean::internalFieldCount, 0, 0, 0, 0, 0, 0);
    else
        defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "SVGAnimatedBoolean", v8::Local<v8::FunctionTemplate>(), V8SVGAnimatedBoolean::internalFieldCount,
            0, 0,
            V8SVGAnimatedBooleanAccessors, WTF_ARRAY_LENGTH(V8SVGAnimatedBooleanAccessors),
            0, 0);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8SVGAnimatedBoolean::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8SVGAnimatedBooleanTemplate);
}

bool V8SVGAnimatedBoolean::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8SVGAnimatedBoolean::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

SVGAnimatedBoolean* V8SVGAnimatedBoolean::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8SVGAnimatedBoolean::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<SVGAnimatedBoolean>()->ref();
#endif
}

void V8SVGAnimatedBoolean::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<SVGAnimatedBoolean>()->deref();
#endif
}

} // namespace blink
