// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8CSSSupportsRule.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8CSSRuleList.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8HiddenValue.h"
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
const WrapperTypeInfo V8CSSSupportsRule::wrapperTypeInfo = { gin::kEmbedderBlink, V8CSSSupportsRule::domTemplate, V8CSSSupportsRule::refObject, V8CSSSupportsRule::derefObject, V8CSSSupportsRule::trace, 0, 0, V8CSSSupportsRule::preparePrototypeObject, V8CSSSupportsRule::installConditionallyEnabledProperties, "CSSSupportsRule", &V8CSSRule::wrapperTypeInfo, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Dependent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in CSSSupportsRule.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& CSSSupportsRule::s_wrapperTypeInfo = V8CSSSupportsRule::wrapperTypeInfo;

namespace CSSSupportsRuleV8Internal {

static void conditionTextAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    CSSSupportsRule* impl = V8CSSSupportsRule::toImpl(holder);
    v8SetReturnValueString(info, impl->conditionText(), info.GetIsolate());
}

static void conditionTextAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    CSSSupportsRuleV8Internal::conditionTextAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void cssRulesAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    CSSSupportsRule* impl = V8CSSSupportsRule::toImpl(holder);
    RefPtrWillBeRawPtr<CSSRuleList> cppValue(impl->cssRules());
    if (cppValue && DOMDataStore::setReturnValue(info.GetReturnValue(), cppValue.get()))
        return;
    v8::Local<v8::Value> v8Value(toV8(cppValue.get(), holder, info.GetIsolate()));
    if (!v8Value.IsEmpty()) {
        V8HiddenValue::setHiddenValue(info.GetIsolate(), holder, v8AtomicString(info.GetIsolate(), "cssRules"), v8Value);
        v8SetReturnValue(info, v8Value);
    }
}

static void cssRulesAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    CSSSupportsRuleV8Internal::cssRulesAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void insertRuleMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "insertRule", "CSSSupportsRule", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 2)) {
        setMinimumArityTypeError(exceptionState, 2, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    CSSSupportsRule* impl = V8CSSSupportsRule::toImpl(info.Holder());
    V8StringResource<> rule;
    unsigned index;
    {
        rule = info[0];
        if (!rule.prepare())
            return;
        index = toUInt32(info.GetIsolate(), info[1], NormalConversion, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    unsigned result = impl->insertRule(rule, index, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValueUnsigned(info, result);
}

static void insertRuleMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    CSSSupportsRuleV8Internal::insertRuleMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void deleteRuleMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "deleteRule", "CSSSupportsRule", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    CSSSupportsRule* impl = V8CSSSupportsRule::toImpl(info.Holder());
    unsigned index;
    {
        index = toUInt32(info.GetIsolate(), info[0], NormalConversion, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    impl->deleteRule(index, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
}

static void deleteRuleMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    CSSSupportsRuleV8Internal::deleteRuleMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace CSSSupportsRuleV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8CSSSupportsRuleAccessors[] = {
    {"conditionText", CSSSupportsRuleV8Internal::conditionTextAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"cssRules", CSSSupportsRuleV8Internal::cssRulesAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static const V8DOMConfiguration::MethodConfiguration V8CSSSupportsRuleMethods[] = {
    {"insertRule", CSSSupportsRuleV8Internal::insertRuleMethodCallback, 0, 2, V8DOMConfiguration::ExposedToAllScripts},
    {"deleteRule", CSSSupportsRuleV8Internal::deleteRuleMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
};

static void installV8CSSSupportsRuleTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "CSSSupportsRule", V8CSSRule::domTemplate(isolate), V8CSSSupportsRule::internalFieldCount,
        0, 0,
        V8CSSSupportsRuleAccessors, WTF_ARRAY_LENGTH(V8CSSSupportsRuleAccessors),
        V8CSSSupportsRuleMethods, WTF_ARRAY_LENGTH(V8CSSSupportsRuleMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8CSSSupportsRule::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8CSSSupportsRuleTemplate);
}

bool V8CSSSupportsRule::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8CSSSupportsRule::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

CSSSupportsRule* V8CSSSupportsRule::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8CSSSupportsRule::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<CSSSupportsRule>()->ref();
#endif
}

void V8CSSSupportsRule::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<CSSSupportsRule>()->deref();
#endif
}

} // namespace blink
