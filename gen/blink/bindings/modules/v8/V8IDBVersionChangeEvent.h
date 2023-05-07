// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#ifndef V8IDBVersionChangeEvent_h
#define V8IDBVersionChangeEvent_h

#include "bindings/core/v8/ScriptWrappable.h"
#include "bindings/core/v8/ToV8.h"
#include "bindings/core/v8/V8Binding.h"
#include "bindings/core/v8/V8DOMWrapper.h"
#include "bindings/core/v8/V8Event.h"
#include "bindings/core/v8/WrapperTypeInfo.h"
#include "modules/ModulesExport.h"
#include "modules/indexeddb/IDBVersionChangeEvent.h"
#include "platform/heap/Handle.h"

namespace blink {

class V8IDBVersionChangeEvent {
public:
    MODULES_EXPORT static bool hasInstance(v8::Local<v8::Value>, v8::Isolate*);
    static v8::Local<v8::Object> findInstanceInPrototypeChain(v8::Local<v8::Value>, v8::Isolate*);
    MODULES_EXPORT static v8::Local<v8::FunctionTemplate> domTemplate(v8::Isolate*);
    static IDBVersionChangeEvent* toImpl(v8::Local<v8::Object> object)
    {
        return toScriptWrappable(object)->toImpl<IDBVersionChangeEvent>();
    }
    MODULES_EXPORT static IDBVersionChangeEvent* toImplWithTypeCheck(v8::Isolate*, v8::Local<v8::Value>);
    MODULES_EXPORT static const WrapperTypeInfo wrapperTypeInfo;
    static void refObject(ScriptWrappable*);
    static void derefObject(ScriptWrappable*);
    template<typename VisitorDispatcher>
    static void trace(VisitorDispatcher visitor, ScriptWrappable* scriptWrappable)
    {
#if ENABLE(OILPAN)
        visitor->trace(scriptWrappable->toImpl<IDBVersionChangeEvent>());
#endif
    }
    static void constructorCallback(const v8::FunctionCallbackInfo<v8::Value>&);
    static const int internalFieldCount = v8DefaultWrapperInternalFieldCount + 0;
    static void installConditionallyEnabledProperties(v8::Local<v8::Object>, v8::Isolate*) { }
    static void preparePrototypeObject(v8::Isolate*, v8::Local<v8::Object> prototypeObject, v8::Local<v8::FunctionTemplate> interfaceTemplate) { }
};

template <>
struct V8TypeOf<IDBVersionChangeEvent> {
    typedef V8IDBVersionChangeEvent Type;
};

} // namespace blink

#endif // V8IDBVersionChangeEvent_h
