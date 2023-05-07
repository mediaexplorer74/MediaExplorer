// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8Event.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8EventInit.h"
#include "bindings/core/v8/V8EventTarget.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "core/frame/LocalDOMWindow.h"
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
const WrapperTypeInfo V8Event::wrapperTypeInfo = { gin::kEmbedderBlink, V8Event::domTemplate, V8Event::refObject, V8Event::derefObject, V8Event::trace, 0, 0, V8Event::preparePrototypeObject, V8Event::installConditionallyEnabledProperties, "Event", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in Event.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& Event::s_wrapperTypeInfo = V8Event::wrapperTypeInfo;

namespace EventV8Internal {

static void typeAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueString(info, impl->type(), info.GetIsolate());
}

static void typeAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::typeAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void targetAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueFast(info, WTF::getPtr(impl->target()), impl);
}

static void targetAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::targetAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void currentTargetAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueFast(info, WTF::getPtr(impl->currentTarget()), impl);
}

static void currentTargetAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::currentTargetAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void eventPhaseAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueUnsigned(info, impl->eventPhase());
}

static void eventPhaseAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::eventPhaseAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void bubblesAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueBool(info, impl->bubbles());
}

static void bubblesAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::bubblesAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void cancelableAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueBool(info, impl->cancelable());
}

static void cancelableAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::cancelableAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void defaultPreventedAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueBool(info, impl->defaultPrevented());
}

static void defaultPreventedAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::defaultPreventedAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void timeStampAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValue(info, static_cast<double>(impl->timeStamp()));
}

static void timeStampAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    EventV8Internal::timeStampAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void pathAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    v8SetReturnValue(info, toV8(impl->path(scriptState), info.Holder(), info.GetIsolate()));
}

static void pathAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventPath);
    EventV8Internal::pathAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void srcElementAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueFast(info, WTF::getPtr(impl->srcElement()), impl);
}

static void srcElementAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventSrcElement);
    EventV8Internal::srcElementAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void returnValueAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    ExecutionContext* executionContext = currentExecutionContext(info.GetIsolate());
    v8SetReturnValueBool(info, impl->legacyReturnValue(executionContext));
}

static void returnValueAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventReturnValue);
    EventV8Internal::returnValueAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void returnValueAttributeSetter(v8::Local<v8::Value> v8Value, const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    ExceptionState exceptionState(ExceptionState::SetterContext, "returnValue", "Event", holder, info.GetIsolate());
    Event* impl = V8Event::toImpl(holder);
    bool cppValue = toBoolean(info.GetIsolate(), v8Value, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    ExecutionContext* executionContext = currentExecutionContext(info.GetIsolate());
    impl->setLegacyReturnValue(executionContext, cppValue);
}

static void returnValueAttributeSetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Value> v8Value = info[0];
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMSetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventReturnValue);
    EventV8Internal::returnValueAttributeSetter(v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void cancelBubbleAttributeGetter(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    Event* impl = V8Event::toImpl(holder);
    v8SetReturnValueBool(info, impl->cancelBubble());
}

static void cancelBubbleAttributeGetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMGetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventCancelBubble);
    EventV8Internal::cancelBubbleAttributeGetter(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void cancelBubbleAttributeSetter(v8::Local<v8::Value> v8Value, const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Object> holder = info.Holder();
    ExceptionState exceptionState(ExceptionState::SetterContext, "cancelBubble", "Event", holder, info.GetIsolate());
    Event* impl = V8Event::toImpl(holder);
    bool cppValue = toBoolean(info.GetIsolate(), v8Value, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    impl->setCancelBubble(cppValue);
}

static void cancelBubbleAttributeSetterCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    v8::Local<v8::Value> v8Value = info[0];
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMSetter");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::EventCancelBubble);
    EventV8Internal::cancelBubbleAttributeSetter(v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void stopPropagationMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    Event* impl = V8Event::toImpl(info.Holder());
    impl->stopPropagation();
}

static void stopPropagationMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventV8Internal::stopPropagationMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void stopImmediatePropagationMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    Event* impl = V8Event::toImpl(info.Holder());
    impl->stopImmediatePropagation();
}

static void stopImmediatePropagationMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventV8Internal::stopImmediatePropagationMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void preventDefaultMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    Event* impl = V8Event::toImpl(info.Holder());
    impl->preventDefault();
}

static void preventDefaultMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    EventV8Internal::preventDefaultMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void initEventMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "initEvent", "Event", info.Holder(), info.GetIsolate());
    Event* impl = V8Event::toImpl(info.Holder());
    V8StringResource<> type;
    bool bubbles;
    bool cancelable;
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
    }
    impl->initEvent(type, bubbles, cancelable);
}

static void initEventMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    UseCounter::countIfNotPrivateScript(info.GetIsolate(), callingExecutionContext(info.GetIsolate()), UseCounter::V8Event_InitEvent_Method);
    EventV8Internal::initEventMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void constructor(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ConstructionContext, "Event", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    V8StringResource<> type;
    EventInit eventInitDict;
    {
        type = info[0];
        if (!type.prepare())
            return;
        if (!isUndefinedOrNull(info[1]) && !info[1]->IsObject()) {
            exceptionState.throwTypeError("parameter 2 ('eventInitDict') is not an object.");
            exceptionState.throwIfNeeded();
            return;
        }
        V8EventInit::toImpl(info.GetIsolate(), info[1], eventInitDict, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    RefPtrWillBeRawPtr<Event> impl = Event::create(type, eventInitDict);
    v8::Local<v8::Object> wrapper = info.Holder();
    wrapper = impl->associateWithWrapper(info.GetIsolate(), &V8Event::wrapperTypeInfo, wrapper);
    v8SetReturnValue(info, wrapper);
}

} // namespace EventV8Internal

static const V8DOMConfiguration::AccessorConfiguration V8EventAccessors[] = {
    {"type", EventV8Internal::typeAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"target", EventV8Internal::targetAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"currentTarget", EventV8Internal::currentTargetAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"eventPhase", EventV8Internal::eventPhaseAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"bubbles", EventV8Internal::bubblesAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"cancelable", EventV8Internal::cancelableAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"defaultPrevented", EventV8Internal::defaultPreventedAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"timeStamp", EventV8Internal::timeStampAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"path", EventV8Internal::pathAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"srcElement", EventV8Internal::srcElementAttributeGetterCallback, 0, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"returnValue", EventV8Internal::returnValueAttributeGetterCallback, EventV8Internal::returnValueAttributeSetterCallback, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
    {"cancelBubble", EventV8Internal::cancelBubbleAttributeGetterCallback, EventV8Internal::cancelBubbleAttributeSetterCallback, 0, 0, 0, static_cast<v8::AccessControl>(v8::DEFAULT), static_cast<v8::PropertyAttribute>(v8::None), V8DOMConfiguration::ExposedToAllScripts, V8DOMConfiguration::OnPrototype, V8DOMConfiguration::CheckHolder},
};

static const V8DOMConfiguration::MethodConfiguration V8EventMethods[] = {
    {"stopPropagation", EventV8Internal::stopPropagationMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"stopImmediatePropagation", EventV8Internal::stopImmediatePropagationMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"preventDefault", EventV8Internal::preventDefaultMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"initEvent", EventV8Internal::initEventMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
};

void V8Event::constructorCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SCOPED_SAMPLING_STATE("blink", "DOMConstructor");
    if (!info.IsConstructCall()) {
        V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::constructorNotCallableAsFunction("Event"));
        return;
    }

    if (ConstructorMode::current(info.GetIsolate()) == ConstructorMode::WrapExistingObject) {
        v8SetReturnValue(info, info.Holder());
        return;
    }

    EventV8Internal::constructor(info);
}

static void installV8EventTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "Event", v8::Local<v8::FunctionTemplate>(), V8Event::internalFieldCount,
        0, 0,
        V8EventAccessors, WTF_ARRAY_LENGTH(V8EventAccessors),
        V8EventMethods, WTF_ARRAY_LENGTH(V8EventMethods));
    functionTemplate->SetCallHandler(V8Event::constructorCallback);
    functionTemplate->SetLength(1);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
    static const V8DOMConfiguration::ConstantConfiguration V8EventConstants[] = {
        {"NONE", 0, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"CAPTURING_PHASE", 1, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"AT_TARGET", 2, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"BUBBLING_PHASE", 3, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEDOWN", 1, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEUP", 2, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEOVER", 4, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEOUT", 8, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEMOVE", 16, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"MOUSEDRAG", 32, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"CLICK", 64, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"DBLCLICK", 128, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"KEYDOWN", 256, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"KEYUP", 512, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"KEYPRESS", 1024, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"DRAGDROP", 2048, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"FOCUS", 4096, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"BLUR", 8192, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"SELECT", 16384, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
        {"CHANGE", 32768, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort},
    };
    V8DOMConfiguration::installConstants(isolate, functionTemplate, prototypeTemplate, V8EventConstants, WTF_ARRAY_LENGTH(V8EventConstants));
    static_assert(0 == Event::NONE, "the value of Event_NONE does not match with implementation");
    static_assert(1 == Event::CAPTURING_PHASE, "the value of Event_CAPTURING_PHASE does not match with implementation");
    static_assert(2 == Event::AT_TARGET, "the value of Event_AT_TARGET does not match with implementation");
    static_assert(3 == Event::BUBBLING_PHASE, "the value of Event_BUBBLING_PHASE does not match with implementation");
    static_assert(1 == Event::MOUSEDOWN, "the value of Event_MOUSEDOWN does not match with implementation");
    static_assert(2 == Event::MOUSEUP, "the value of Event_MOUSEUP does not match with implementation");
    static_assert(4 == Event::MOUSEOVER, "the value of Event_MOUSEOVER does not match with implementation");
    static_assert(8 == Event::MOUSEOUT, "the value of Event_MOUSEOUT does not match with implementation");
    static_assert(16 == Event::MOUSEMOVE, "the value of Event_MOUSEMOVE does not match with implementation");
    static_assert(32 == Event::MOUSEDRAG, "the value of Event_MOUSEDRAG does not match with implementation");
    static_assert(64 == Event::CLICK, "the value of Event_CLICK does not match with implementation");
    static_assert(128 == Event::DBLCLICK, "the value of Event_DBLCLICK does not match with implementation");
    static_assert(256 == Event::KEYDOWN, "the value of Event_KEYDOWN does not match with implementation");
    static_assert(512 == Event::KEYUP, "the value of Event_KEYUP does not match with implementation");
    static_assert(1024 == Event::KEYPRESS, "the value of Event_KEYPRESS does not match with implementation");
    static_assert(2048 == Event::DRAGDROP, "the value of Event_DRAGDROP does not match with implementation");
    static_assert(4096 == Event::FOCUS, "the value of Event_FOCUS does not match with implementation");
    static_assert(8192 == Event::BLUR, "the value of Event_BLUR does not match with implementation");
    static_assert(16384 == Event::SELECT, "the value of Event_SELECT does not match with implementation");
    static_assert(32768 == Event::CHANGE, "the value of Event_CHANGE does not match with implementation");

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8Event::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8EventTemplate);
}

bool V8Event::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8Event::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

Event* V8Event::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8Event::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<Event>()->ref();
#endif
}

void V8Event::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<Event>()->deref();
#endif
}

} // namespace blink
