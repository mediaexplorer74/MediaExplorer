// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#ifndef V8WheelEventInit_h
#define V8WheelEventInit_h

#include "bindings/core/v8/ToV8.h"
#include "bindings/core/v8/V8Binding.h"
#include "core/CoreExport.h"
#include "core/events/WheelEventInit.h"
#include "platform/heap/Handle.h"

namespace blink {

class ExceptionState;

class V8WheelEventInit {
public:
    CORE_EXPORT static void toImpl(v8::Isolate*, v8::Local<v8::Value>, WheelEventInit&, ExceptionState&);
};

v8::Local<v8::Value> toV8(const WheelEventInit&, v8::Local<v8::Object>, v8::Isolate*);
CORE_EXPORT bool toV8WheelEventInit(const WheelEventInit&, v8::Local<v8::Object> dictionary, v8::Local<v8::Object> creationContext, v8::Isolate*);

template<class CallbackInfo>
inline void v8SetReturnValue(const CallbackInfo& callbackInfo, WheelEventInit& impl)
{
    v8SetReturnValue(callbackInfo, toV8(impl, callbackInfo.Holder(), callbackInfo.GetIsolate()));
}

template <>
struct NativeValueTraits<WheelEventInit> {
    static WheelEventInit nativeValue(v8::Isolate*, v8::Local<v8::Value>, ExceptionState&);
};

} // namespace blink

#endif // V8WheelEventInit_h
