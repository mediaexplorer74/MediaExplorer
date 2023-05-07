// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8SVGPoint.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/core/v8/V8SVGElement.h"
#include "bindings/core/v8/V8SVGMatrix.h"
#include "bindings/core/v8/V8SVGPoint.h"
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
const WrapperTypeInfo V8SVGPoint::wrapperTypeInfo = { gin::kEmbedderBlink, V8SVGPoint::domTemplate, V8SVGPoint::refObject, V8SVGPoint::derefObject, V8SVGPoint::trace, 0, V8SVGPoint::visitDOMWrapper, V8SVGPoint::preparePrototypeObject, V8SVGPoint::installConditionallyEnabledProperties, "SVGPoint", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Dependent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in SVGPointTearOff.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& SVGPointTearOff::s_wrapperTypeInfo = V8SVGPoint::wrapperTypeInfo;

namespace SVGPointTearOffV8Internal {

static void xAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    SVGPointTearOff* impl = V8SVGPoint::toImpl(holder);
    v8SetReturnValue(info, impl->x());
}

static void xAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    SVGPointTearOffV8Internal::xAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void xAttributeSetter(v8::Local<v8::Value> v8Value, const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    ExceptionState exceptionState(ExceptionState::SetterContext, "x", "SVGPoint", holder, info.GetIsolate());
    SVGPointTearOff* impl = V8SVGPoint::toImpl(holder);
    float cppValue = toFloat(info.GetIsolate(), v8Value, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    impl->setX(cppValue, exceptionState);
    exceptionState.throwIfNeeded();
}

static void xAttributeSetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Value> v8Value = info[0];
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMSetter");
    SVGPointTearOffV8Internal::xAttributeSetter(v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void yAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    SVGPointTearOff* impl = V8SVGPoint::toImpl(holder);
    v8SetReturnValue(info, impl->y());
}

static void yAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    SVGPointTearOffV8Internal::yAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void yAttributeSetter(v8::Local<v8::Value> v8Value, const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    ExceptionState exceptionState(ExceptionState::SetterContext, "y", "SVGPoint", holder, info.GetIsolate());
    SVGPointTearOff* impl = V8SVGPoint::toImpl(holder);
    float cppValue = toFloat(info.GetIsolate(), v8Value, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    impl->setY(cppValue, exceptionState);
    exceptionState.throwIfNeeded();
}

static void yAttributeSetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Value> v8Value = info[0];
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMSetter");
    SVGPointTearOffV8Internal::yAttributeSetter(v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void matrixTransformMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    if (UNLIKELY(info.Length() < 1)) {
        V8ThrowException::throwException(createMinimumArityTypeErrorForMethod(info.GetIsolate(), "matrixTransform", "SVGPoint", 1, info.Length()), info.GetIsolate());
        return;
    }
    SVGPointTearOff* impl = V8SVGPoint::toImpl(info.Holder());
    SVGMatrixTearOff* matrix;
    {
        matrix = V8SVGMatrix::toImplWithTypeCheck(info.GetIsolate(), info[0]);
        if (!matrix) {
            V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::failedToExecute("matrixTransform", "SVGPoint", "parameter 1 is not of type 'SVGMatrix'."));
            return;
        }
    }
    v8SetReturnValue(info, impl->matrixTransform(matrix));
}

static void matrixTransformMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::SVGPointMatrixTransform);
    SVGPointTearOffV8Internal::matrixTransformMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace SVGPointTearOffV8Internal

void V8SVGPoint::visitDOMWrapper(v8::Isolate* isolate, ScriptWrappable* scriptWrappable, const v8::Persistent<v8::Object>& wrapper)
{
    SVGPointTearOff* impl = scriptWrappable->toImpl<SVGPointTearOff>();
    v8::Local<v8::Object> creationContext = v8::Local<v8::Object>::New(isolate, wrapper);
    V8WrapperInstantiationScope scope(creationContext, isolate);
    SVGElement* contextElement = impl->contextElement();
    if (contextElement) {
        if (DOMDataStore::containsWrapper(contextElement, isolate))
            DOMDataStore::setWrapperReference(wrapper, contextElement, isolate);
    }
}

static const V8DOMConfiguration::AccessorConfiguration V8SVGPointAccessors[] = {
    {"x", SVGPointTearOffV8Internal::xAttributeGetterCallback, SVGPointTearOffV8Internal::xAttributeSetterCallback, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"y", SVGPointTearOffV8Internal::yAttributeGetterCallback, SVGPointTearOffV8Internal::yAttributeSetterCallback, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static const V8DOMConfiguration::MethodConfiguration V8SVGPointMethods[] = {
    {"matrixTransform", SVGPointTearOffV8Internal::matrixTransformMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8SVGPointTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "SVGPoint", v8::Local<v8::FunctionTemplate>(), V8SVGPoint::internalFieldCount,
        0, 0,
        V8SVGPointAccessors, WTF_ARRAY_LENGTH(V8SVGPointAccessors),
        V8SVGPointMethods, WTF_ARRAY_LENGTH(V8SVGPointMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8SVGPoint::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8SVGPointTemplate);
}

bool V8SVGPoint::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8SVGPoint::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

SVGPointTearOff* V8SVGPoint::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8SVGPoint::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<SVGPointTearOff>()->ref();
#endif
}

void V8SVGPoint::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<SVGPointTearOff>()->deref();
#endif
}

} // namespace blink
