// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8XPathExpression.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/ScriptValue.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8Node.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/core/v8/V8XPathResult.h"
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
const WrapperTypeInfo V8XPathExpression::wrapperTypeInfo = { gin::kEmbedderBlink, V8XPathExpression::domTemplate, V8XPathExpression::refObject, V8XPathExpression::derefObject, V8XPathExpression::trace, 0, 0, V8XPathExpression::preparePrototypeObject, V8XPathExpression::installConditionallyEnabledProperties, "XPathExpression", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::GarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in XPathExpression.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& XPathExpression::s_wrapperTypeInfo = V8XPathExpression::wrapperTypeInfo;

namespace XPathExpressionV8Internal {

static void evaluateMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "evaluate", "XPathExpression", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    XPathExpression* impl = V8XPathExpression::toImpl(info.Holder());
    Node* contextNode;
    unsigned type;
    ScriptValue inResult;
    {
        contextNode = V8Node::toImplWithTypeCheck(info.GetIsolate(), info[0]);
        if (!contextNode) {
            exceptionState.throwTypeError("parameter 1 is not of type 'Node'.");
            exceptionState.throwIfNeeded();
            return;
        }
        if (!info[1]->IsUndefined()) {
            type = toUInt16(info.GetIsolate(), info[1], NormalConversion, exceptionState);
            if (exceptionState.throwIfNeeded())
                return;
        } else {
            type = 0u;
        }
        if (!info[2]->IsUndefined()) {
            inResult = ScriptValue(ScriptState::current(info.GetIsolate()), info[2]);
        } else {
            inResult = ScriptValue();
        }
    }
    RawPtr<XPathResult> result = impl->evaluate(contextNode, type, inResult, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void evaluateMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    XPathExpressionV8Internal::evaluateMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace XPathExpressionV8Internal

static const V8DOMConfiguration::MethodConfiguration V8XPathExpressionMethods[] = {
    {"evaluate", XPathExpressionV8Internal::evaluateMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8XPathExpressionTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "XPathExpression", v8::Local<v8::FunctionTemplate>(), V8XPathExpression::internalFieldCount,
        0, 0,
        0, 0,
        V8XPathExpressionMethods, WTF_ARRAY_LENGTH(V8XPathExpressionMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8XPathExpression::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8XPathExpressionTemplate);
}

bool V8XPathExpression::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8XPathExpression::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

XPathExpression* V8XPathExpression::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8XPathExpression::refObject(ScriptWrappable* scriptWrappable)
{
}

void V8XPathExpression::derefObject(ScriptWrappable* scriptWrappable)
{
}

} // namespace blink
