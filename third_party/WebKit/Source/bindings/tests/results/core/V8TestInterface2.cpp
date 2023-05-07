// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8TestInterface2.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/ScriptState.h"
#include "bindings/core/v8/ScriptValue.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8GCController.h"
#include "bindings/core/v8/V8Iterator.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/core/v8/V8TestInterfaceEmpty.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "core/dom/Element.h"
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
const WrapperTypeInfo V8TestInterface2::wrapperTypeInfo = { gin::kEmbedderBlink, V8TestInterface2::domTemplate, V8TestInterface2::refObject, V8TestInterface2::derefObject, V8TestInterface2::trace, 0, V8TestInterface2::visitDOMWrapper, V8TestInterface2::preparePrototypeObject, V8TestInterface2::installConditionallyEnabledProperties, "TestInterface2", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Dependent, WrapperTypeInfo::RefCountedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in TestInterface2.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& TestInterface2::s_wrapperTypeInfo = V8TestInterface2::wrapperTypeInfo;

namespace TestInterface2V8Internal {

static void itemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "item", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    unsigned index;
    {
        index = toUInt32(info.GetIsolate(), info[0], NormalConversion, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    RefPtr<TestInterfaceEmpty> result = impl->item(index, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void itemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::itemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void setItemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "setItem", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 2)) {
        setMinimumArityTypeError(exceptionState, 2, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    unsigned index;
    TestInterfaceEmpty* value;
    {
        index = toUInt32(info.GetIsolate(), info[0], NormalConversion, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
        value = V8TestInterfaceEmpty::toImplWithTypeCheck(info.GetIsolate(), info[1]);
        if (!value) {
            exceptionState.throwTypeError("parameter 2 is not of type 'TestInterfaceEmpty'.");
            exceptionState.throwIfNeeded();
            return;
        }
    }
    RefPtr<TestInterfaceEmpty> result = impl->setItem(index, value, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void setItemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::setItemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void deleteItemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "deleteItem", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    unsigned index;
    {
        index = toUInt32(info.GetIsolate(), info[0], NormalConversion, exceptionState);
        if (exceptionState.throwIfNeeded())
            return;
    }
    bool result = impl->deleteItem(index, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValueBool(info, result);
}

static void deleteItemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::deleteItemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedItemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "namedItem", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    V8StringResource<> name;
    {
        name = info[0];
        if (!name.prepare())
            return;
    }
    RefPtr<TestInterfaceEmpty> result = impl->namedItem(name, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void namedItemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::namedItemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void setNamedItemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "setNamedItem", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 2)) {
        setMinimumArityTypeError(exceptionState, 2, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    V8StringResource<> name;
    TestInterfaceEmpty* value;
    {
        name = info[0];
        if (!name.prepare())
            return;
        value = V8TestInterfaceEmpty::toImplWithTypeCheck(info.GetIsolate(), info[1]);
        if (!value && !isUndefinedOrNull(info[1])) {
            exceptionState.throwTypeError("parameter 2 is not of type 'TestInterfaceEmpty'.");
            exceptionState.throwIfNeeded();
            return;
        }
    }
    RefPtr<TestInterfaceEmpty> result = impl->setNamedItem(name, value, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void setNamedItemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::setNamedItemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void deleteNamedItemMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "deleteNamedItem", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    V8StringResource<> name;
    {
        name = info[0];
        if (!name.prepare())
            return;
    }
    bool result = impl->deleteNamedItem(name, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValueBool(info, result);
}

static void deleteNamedItemMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::deleteNamedItemMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void stringifierMethodMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    v8SetReturnValueString(info, impl->stringifierMethod(), info.GetIsolate());
}

static void stringifierMethodMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::stringifierMethodMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void keysMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "keys", "TestInterface2", info.Holder(), info.GetIsolate());
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    RawPtr<Iterator> result = impl->keysForBinding(scriptState, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void keysMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::keysMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void valuesMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "values", "TestInterface2", info.Holder(), info.GetIsolate());
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    RawPtr<Iterator> result = impl->valuesForBinding(scriptState, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void valuesMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::valuesMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void entriesMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "entries", "TestInterface2", info.Holder(), info.GetIsolate());
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    RawPtr<Iterator> result = impl->entriesForBinding(scriptState, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void entriesMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::entriesMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void forEachMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "forEach", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ScriptValue callback;
    ScriptValue thisArg;
    {
        if (!info[0]->IsFunction()) {
            exceptionState.throwTypeError("The callback provided as parameter 1 is not a function.");
            exceptionState.throwIfNeeded();
            return;
        }
        callback = ScriptValue(ScriptState::current(info.GetIsolate()), info[0]);
        thisArg = ScriptValue(ScriptState::current(info.GetIsolate()), info[1]);
    }
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    impl->forEachForBinding(scriptState, ScriptValue(scriptState, info.This()), callback, thisArg, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
}

static void forEachMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::forEachMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void hasMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "has", "TestInterface2", info.Holder(), info.GetIsolate());
    if (UNLIKELY(info.Length() < 1)) {
        setMinimumArityTypeError(exceptionState, 1, info.Length());
        exceptionState.throwIfNeeded();
        return;
    }
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    TestInterfaceEmpty* value;
    {
        value = V8TestInterfaceEmpty::toImplWithTypeCheck(info.GetIsolate(), info[0]);
    }
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    bool result = impl->hasForBinding(scriptState, value, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValueBool(info, result);
}

static void hasMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::hasMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void toStringMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    v8SetReturnValueString(info, impl->stringifierMethod(), info.GetIsolate());
}

static void toStringMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::toStringMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void iteratorMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    ExceptionState exceptionState(ExceptionState::ExecutionContext, "iterator", "TestInterface2", info.Holder(), info.GetIsolate());
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ScriptState* scriptState = ScriptState::current(info.GetIsolate());
    RawPtr<Iterator> result = impl->iterator(scriptState, exceptionState);
    if (exceptionState.hadException()) {
        exceptionState.throwIfNeeded();
        return;
    }
    v8SetReturnValue(info, result.release());
}

static void iteratorMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    TestInterface2V8Internal::iteratorMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void constructor(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    RefPtr<TestInterface2> impl = TestInterface2::create();
    v8::Local<v8::Object> wrapper = info.Holder();
    wrapper = impl->associateWithWrapper(info.GetIsolate(), &V8TestInterface2::wrapperTypeInfo, wrapper);
    v8SetReturnValue(info, wrapper);
}

static void indexedPropertyGetter(uint32_t index, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ExceptionState exceptionState(ExceptionState::IndexedGetterContext, "TestInterface2", info.Holder(), info.GetIsolate());
    RefPtr<TestInterfaceEmpty> result = impl->item(index, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (!result)
        return;
    v8SetReturnValueFast(info, WTF::getPtr(result.release()), impl);
}

static void indexedPropertyGetterCallback(uint32_t index, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMIndexedProperty");
    TestInterface2V8Internal::indexedPropertyGetter(index, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void indexedPropertySetter(uint32_t index, v8::Local<v8::Value> v8Value, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    TestInterfaceEmpty* propertyValue = V8TestInterfaceEmpty::toImplWithTypeCheck(info.GetIsolate(), v8Value);
    ExceptionState exceptionState(ExceptionState::IndexedSetterContext, "TestInterface2", info.Holder(), info.GetIsolate());
    if (!propertyValue) {
        exceptionState.throwTypeError("The provided value is not of type 'TestInterfaceEmpty'.");
        exceptionState.throwIfNeeded();
        return;
    }
    bool result = impl->setItem(index, propertyValue, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (!result)
        return;
    v8SetReturnValue(info, v8Value);
}

static void indexedPropertySetterCallback(uint32_t index, v8::Local<v8::Value> v8Value, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMIndexedProperty");
    TestInterface2V8Internal::indexedPropertySetter(index, v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void indexedPropertyDeleter(uint32_t index, const v8::PropertyCallbackInfo<v8::Boolean>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    ExceptionState exceptionState(ExceptionState::IndexedDeletionContext, "TestInterface2", info.Holder(), info.GetIsolate());
    DeleteResult result = impl->deleteItem(index, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (result != DeleteUnknownProperty)
        return v8SetReturnValueBool(info, result == DeleteSuccess);
}

static void indexedPropertyDeleterCallback(uint32_t index, const v8::PropertyCallbackInfo<v8::Boolean>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMIndexedProperty");
    TestInterface2V8Internal::indexedPropertyDeleter(index, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedPropertyGetter(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    auto nameString = name.As<v8::String>();
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    AtomicString propertyName = toCoreAtomicString(nameString);
    v8::String::Utf8Value namedProperty(nameString);
    ExceptionState exceptionState(ExceptionState::GetterContext, *namedProperty, "TestInterface2", info.Holder(), info.GetIsolate());
    RefPtr<TestInterfaceEmpty> result = impl->namedItem(propertyName, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (!result)
        return;
    v8SetReturnValueFast(info, WTF::getPtr(result.release()), impl);
}

static void namedPropertyGetterCallback(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMNamedProperty");
    TestInterface2V8Internal::namedPropertyGetter(name, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedPropertySetter(v8::Local<v8::Name> name, v8::Local<v8::Value> v8Value, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    auto nameString = name.As<v8::String>();
    v8::String::Utf8Value namedProperty(nameString);
    ExceptionState exceptionState(ExceptionState::SetterContext, *namedProperty, "TestInterface2", info.Holder(), info.GetIsolate());
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    V8StringResource<> propertyName(nameString);
    if (!propertyName.prepare())
        return;
    TestInterfaceEmpty* propertyValue = V8TestInterfaceEmpty::toImplWithTypeCheck(info.GetIsolate(), v8Value);
    if (!propertyValue && !isUndefinedOrNull(v8Value)) {
        exceptionState.throwTypeError("The provided value is not of type 'TestInterfaceEmpty'.");
        exceptionState.throwIfNeeded();
        return;
    }
    bool result = impl->setNamedItem(propertyName, propertyValue, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (!result)
        return;
    v8SetReturnValue(info, v8Value);
}

static void namedPropertySetterCallback(v8::Local<v8::Name> name, v8::Local<v8::Value> v8Value, const v8::PropertyCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMNamedProperty");
    TestInterface2V8Internal::namedPropertySetter(name, v8Value, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedPropertyQuery(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Integer>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    AtomicString propertyName = toCoreAtomicString(name.As<v8::String>());
    v8::String::Utf8Value namedProperty(name);
    ExceptionState exceptionState(ExceptionState::GetterContext, *namedProperty, "TestInterface2", info.Holder(), info.GetIsolate());
    bool result = impl->namedPropertyQuery(propertyName, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (!result)
        return;
    v8SetReturnValueInt(info, v8::None);
}

static void namedPropertyQueryCallback(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Integer>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMNamedProperty");
    TestInterface2V8Internal::namedPropertyQuery(name, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedPropertyDeleter(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Boolean>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    AtomicString propertyName = toCoreAtomicString(name.As<v8::String>());
    v8::String::Utf8Value namedProperty(name);
    ExceptionState exceptionState(ExceptionState::DeletionContext, *namedProperty, "TestInterface2", info.Holder(), info.GetIsolate());
    DeleteResult result = impl->deleteNamedItem(propertyName, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    if (result != DeleteUnknownProperty)
        return v8SetReturnValueBool(info, result == DeleteSuccess);
}

static void namedPropertyDeleterCallback(v8::Local<v8::Name> name, const v8::PropertyCallbackInfo<v8::Boolean>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMNamedProperty");
    TestInterface2V8Internal::namedPropertyDeleter(name, info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

static void namedPropertyEnumerator(const v8::PropertyCallbackInfo<v8::Array>& info)
{
    TestInterface2* impl = V8TestInterface2::toImpl(info.Holder());
    Vector<String> names;
    ExceptionState exceptionState(ExceptionState::EnumerationContext, "TestInterface2", info.Holder(), info.GetIsolate());
    impl->namedPropertyEnumerator(names, exceptionState);
    if (exceptionState.throwIfNeeded())
        return;
    v8::Local<v8::Array> v8names = v8::Array::New(info.GetIsolate(), names.size());
    for (size_t i = 0; i < names.size(); ++i) {
        if (!v8CallBoolean(v8names->Set(info.GetIsolate()->GetCurrentContext(), v8::Integer::New(info.GetIsolate(), i), v8String(info.GetIsolate(), names[i]))))
            return;
    }
    v8SetReturnValue(info, v8names);
}

static void namedPropertyEnumeratorCallback(const v8::PropertyCallbackInfo<v8::Array>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMNamedProperty");
    TestInterface2V8Internal::namedPropertyEnumerator(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace TestInterface2V8Internal

void V8TestInterface2::visitDOMWrapper(v8::Isolate* isolate, ScriptWrappable* scriptWrappable, const v8::Persistent<v8::Object>& wrapper)
{
    TestInterface2* impl = scriptWrappable->toImpl<TestInterface2>();
    // The ownerNode() method may return a reference or a pointer.
    if (Node* owner = WTF::getPtr(impl->ownerNode())) {
        Node* root = V8GCController::opaqueRootForGC(isolate, owner);
        isolate->SetReferenceFromGroup(v8::UniqueId(reinterpret_cast<intptr_t>(root)), wrapper);
        return;
    }
}

static const V8DOMConfiguration::MethodConfiguration V8TestInterface2Methods[] = {
    {"item", TestInterface2V8Internal::itemMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"setItem", TestInterface2V8Internal::setItemMethodCallback, 0, 2, V8DOMConfiguration::ExposedToAllScripts},
    {"deleteItem", TestInterface2V8Internal::deleteItemMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"namedItem", TestInterface2V8Internal::namedItemMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"setNamedItem", TestInterface2V8Internal::setNamedItemMethodCallback, 0, 2, V8DOMConfiguration::ExposedToAllScripts},
    {"deleteNamedItem", TestInterface2V8Internal::deleteNamedItemMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"stringifierMethod", TestInterface2V8Internal::stringifierMethodMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"keys", TestInterface2V8Internal::keysMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"values", TestInterface2V8Internal::valuesMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"entries", TestInterface2V8Internal::entriesMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
    {"forEach", TestInterface2V8Internal::forEachMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"has", TestInterface2V8Internal::hasMethodCallback, 0, 1, V8DOMConfiguration::ExposedToAllScripts},
    {"toString", TestInterface2V8Internal::toStringMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
};

void V8TestInterface2::constructorCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SCOPED_SAMPLING_STATE("blink", "DOMConstructor");
    if (!info.IsConstructCall()) {
        V8ThrowException::throwTypeError(info.GetIsolate(), ExceptionMessages::constructorNotCallableAsFunction("TestInterface2"));
        return;
    }

    if (ConstructorMode::current(info.GetIsolate()) == ConstructorMode::WrapExistingObject) {
        v8SetReturnValue(info, info.Holder());
        return;
    }

    TestInterface2V8Internal::constructor(info);
}

static void installV8TestInterface2Template(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "TestInterface2", v8::Local<v8::FunctionTemplate>(), V8TestInterface2::internalFieldCount,
        0, 0,
        0, 0,
        V8TestInterface2Methods, WTF_ARRAY_LENGTH(V8TestInterface2Methods));
    functionTemplate->SetCallHandler(V8TestInterface2::constructorCallback);
    functionTemplate->SetLength(0);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
    if (RuntimeEnabledFeatures::featureNameEnabled()) {
        static const V8DOMConfiguration::ConstantConfiguration constantConfiguration = {"CONST_VALUE_1", 1, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedShort};
        V8DOMConfiguration::installConstant(isolate, functionTemplate, prototypeTemplate, constantConfiguration);
    }
    static_assert(1 == TestInterface2::CONST_VALUE_1, "the value of TestInterface2_CONST_VALUE_1 does not match with implementation");
    {
        v8::IndexedPropertyHandlerConfiguration config(TestInterface2V8Internal::indexedPropertyGetterCallback, TestInterface2V8Internal::indexedPropertySetterCallback, 0, TestInterface2V8Internal::indexedPropertyDeleterCallback, indexedPropertyEnumerator<TestInterface2>);
        functionTemplate->InstanceTemplate()->SetHandler(config);
    }
    {
        v8::NamedPropertyHandlerConfiguration config(TestInterface2V8Internal::namedPropertyGetterCallback, TestInterface2V8Internal::namedPropertySetterCallback, TestInterface2V8Internal::namedPropertyQueryCallback, TestInterface2V8Internal::namedPropertyDeleterCallback, TestInterface2V8Internal::namedPropertyEnumeratorCallback);
        config.flags = static_cast<v8::PropertyHandlerFlags>(static_cast<int>(config.flags) | static_cast<int>(v8::PropertyHandlerFlags::kOnlyInterceptStrings));
        config.flags = static_cast<v8::PropertyHandlerFlags>(static_cast<int>(config.flags) | static_cast<int>(v8::PropertyHandlerFlags::kNonMasking));
        functionTemplate->InstanceTemplate()->SetHandler(config);
    }
    static const V8DOMConfiguration::SymbolKeyedMethodConfiguration symbolKeyedIteratorConfiguration = { v8::Symbol::GetIterator, TestInterface2V8Internal::iteratorMethodCallback, 0, V8DOMConfiguration::ExposedToAllScripts };
    V8DOMConfiguration::installMethod(isolate, prototypeTemplate, defaultSignature, v8::DontDelete, symbolKeyedIteratorConfiguration);

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8TestInterface2::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8TestInterface2Template);
}

bool V8TestInterface2::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8TestInterface2::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

TestInterface2* V8TestInterface2::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8TestInterface2::refObject(ScriptWrappable* scriptWrappable)
{
    scriptWrappable->toImpl<TestInterface2>()->ref();
}

void V8TestInterface2::derefObject(ScriptWrappable* scriptWrappable)
{
    scriptWrappable->toImpl<TestInterface2>()->deref();
}

} // namespace blink
