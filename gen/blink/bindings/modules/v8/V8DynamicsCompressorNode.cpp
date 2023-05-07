// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#if ENABLE(WEB_AUDIO)
#include "V8DynamicsCompressorNode.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8HiddenValue.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/modules/v8/V8AudioParam.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
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
const WrapperTypeInfo V8DynamicsCompressorNode::wrapperTypeInfo = { gin::kEmbedderBlink, V8DynamicsCompressorNode::domTemplate, V8DynamicsCompressorNode::refObject, V8DynamicsCompressorNode::derefObject, V8DynamicsCompressorNode::trace, 0, 0, V8DynamicsCompressorNode::preparePrototypeObject, V8DynamicsCompressorNode::installConditionallyEnabledProperties, "DynamicsCompressorNode", &V8AudioNode::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::InheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::GarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in DynamicsCompressorNode.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& DynamicsCompressorNode::s_wrapperTypeInfo = V8DynamicsCompressorNode::wrapperTypeInfo;

namespace DynamicsCompressorNodeV8Internal {

static void thresholdAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->threshold());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "threshold"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void thresholdAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::thresholdAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void kneeAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->knee());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "knee"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void kneeAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::kneeAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void ratioAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->ratio());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "ratio"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void ratioAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::ratioAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void reductionAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->reduction());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "reduction"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void reductionAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::reductionAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void attackAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->attack());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "attack"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void attackAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::attackAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void releaseAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    DynamicsCompressorNode* impl = V8DynamicsCompressorNode::toImpl(holder);
    RawPtr<AudioParam> cppValue(impl->release());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "release"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void releaseAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    DynamicsCompressorNodeV8Internal::releaseAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace DynamicsCompressorNodeV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8DynamicsCompressorNodeAccessors[] = {
    {"threshold", DynamicsCompressorNodeV8Internal::thresholdAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"knee", DynamicsCompressorNodeV8Internal::kneeAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"ratio", DynamicsCompressorNodeV8Internal::ratioAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"reduction", DynamicsCompressorNodeV8Internal::reductionAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"attack", DynamicsCompressorNodeV8Internal::attackAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"release", DynamicsCompressorNodeV8Internal::releaseAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static void installV8DynamicsCompressorNodeTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "DynamicsCompressorNode", V8AudioNode::domTemplate(isolate), V8DynamicsCompressorNode::internalFieldCount,
        0, 0,
        V8DynamicsCompressorNodeAccessors, WTF_ARRAY_LENGTH(V8DynamicsCompressorNodeAccessors),
        0, 0);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8DynamicsCompressorNode::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8DynamicsCompressorNodeTemplate);
}

bool V8DynamicsCompressorNode::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8DynamicsCompressorNode::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

DynamicsCompressorNode* V8DynamicsCompressorNode::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8DynamicsCompressorNode::refObject(ScriptWrappable* scriptWrappable)
{
}

void V8DynamicsCompressorNode::derefObject(ScriptWrappable* scriptWrappable)
{
}

} // namespace blink
#endif // ENABLE(WEB_AUDIO)
