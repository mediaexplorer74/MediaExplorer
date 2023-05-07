// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8CSS.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
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
const WrapperTypeInfo V8CSS::wrapperTypeInfo = { gin::kEmbedderBlink, V8CSS::domTemplate, V8CSS::refObject, V8CSS::derefObject, V8CSS::trace, 0, 0, V8CSS::preparePrototypeObject, V8CSS::installConditionallyEnabledProperties, "CSS", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in DOMWindowCSS.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& DOMWindowCSS::s_wrapperTypeInfo = V8CSS::wrapperTypeInfo;

namespace DOMWindowCSSV8Internal {

static void supports1Method(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    V8StringResource<> property;
    V8StringResource<> value;
    {
        property = info[0];
        if (!property.prepare())
            return;
        value = info[1];
        if (!value.prepare())
            return;
    }
    v8SetReturnValueBool(info, DOMWindowCSS::supports(property, value));
}

static void supports2Method(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    V8StringResource<> conditionText;
    {
        conditionText = info[0];
        if (!conditionText.prepare())
            return;
    }
    v8SetReturnValueBool(info, DOMWindowCSS::supports(conditionText));
}

static void supportsMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "supports", "CSS", info.Holder(), info.GetIsolate());
    switch (std::min(2, info.Length())) {
    case 1:
        if (true) {
            supports2Method(info);
            return;
        }
        break;
    case 2:
        if (true) {
            supports1Method(info);
            return;
        }
        break;
    default:
        break;
    }
    if (info.Length() < 1) {
        exceptionState.throwTypeError(ExceptionMessages::notEnoughArguments(1, info.Length()));
        exceptionState.throwIfNeeded();
        return;
    }
    exceptionState.throwTypeError("No function was found that matched the signature provided.");
    exceptionState.throwIfNeeded();
    return;
}

static void supportsMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    DOMWindowCSSV8Internal::supportsMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}


static void escapeMethod(const v8::FunctionCallbackInfo<v8::Value>& info) {
    if (UNLIKELY(info.Length() < 1)) {
        V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::failedToExecute("escape", "CSS", ExceptionMessages::notEnoughArguments(1, info.Length())));
        return;
    }

    V8StringResource<> ident;
    ident = info[0];
    if (!ident.prepare())
        return;

    v8SetReturnValueString(info, DOMWindowCSS::escape(ident), info.GetIsolate());
}

static void escapeMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    DOMWindowCSSV8Internal::escapeMethod(info);
}

} // namespace DOMWindowCSSV8Internal

static void installV8CSSTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "CSS", v8::Local<v8::FunctionTemplate>(), V8CSS::internalFieldCount,
        0, 0,
        0, 0,
        0, 0);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
    const V8DOMConfiguration::MethodConfiguration supportsMethodConfiguration = {
        "supports", DOMWindowCSSV8Internal::supportsMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts
    };
    V8DOMConfiguration::installMethod(isolate, functionTemplate, v8::Local<v8::Signature>(), v8::None, supportsMethodConfiguration);

    const V8DOMConfiguration::MethodConfiguration escapeMethodConfiguration = {
        "escape", DOMWindowCSSV8Internal::escapeMethodCallback,     0, 1, V8DOMConfiguration::ExposedToAllScripts
    };
    V8DOMConfiguration::installMethod(isolate, functionTemplate, v8::Local<v8::Signature>(), v8::None, escapeMethodConfiguration);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8CSS::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8CSSTemplate);
}

bool V8CSS::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8CSS::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

DOMWindowCSS* V8CSS::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8CSS::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<DOMWindowCSS>()->ref();
#endif
}

void V8CSS::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<DOMWindowCSS>()->deref();
#endif
}

} // namespace blink
