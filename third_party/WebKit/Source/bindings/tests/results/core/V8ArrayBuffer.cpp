// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8ArrayBuffer.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8ArrayBuffer.h"
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
const WrapperTypeInfo V8ArrayBuffer::wrapperTypeInfo = { gin::kEmbedderBlink, 0, V8ArrayBuffer::refObject, V8ArrayBuffer::derefObject, V8ArrayBuffer::trace, 0, 0, V8ArrayBuffer::preparePrototypeObject, V8ArrayBuffer::installConditionallyEnabledProperties, "ArrayBuffer", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Independent, WrapperTypeInfo::RefCountedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in TestArrayBuffer.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& TestArrayBuffer::s_wrapperTypeInfo = V8ArrayBuffer::wrapperTypeInfo;

bool V8ArrayBuffer::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return v8Value->IsArrayBuffer();
}

TestArrayBuffer* V8ArrayBuffer::toImpl(v8::Local<v8::Object> object)
{
    ASSERT(object->IsArrayBuffer());
    v8::Local<v8::ArrayBuffer> v8buffer = object.As<v8::ArrayBuffer>();
    if (v8buffer->IsExternal()) {
        const WrapperTypeInfo* wrapperTypeInfo = toWrapperTypeInfo(object);
        RELEASE_ASSERT(wrapperTypeInfo);
        RELEASE_ASSERT(wrapperTypeInfo->ginEmbedder == gin::kEmbedderBlink);
        return toScriptWrappable(object)->toImpl<TestArrayBuffer>();
    }

    // Transfer the ownership of the allocated memory to an ArrayBuffer without
    // copying.
    v8::ArrayBuffer::Contents v8Contents = v8buffer->Externalize();
    WTF::ArrayBufferContents contents(v8Contents.Data(), v8Contents.ByteLength(), WTF::ArrayBufferContents::NotShared);
    RefPtr<TestArrayBuffer> buffer = TestArrayBuffer::create(contents);
    v8::Local<v8::Object> associatedWrapper = buffer->associateWithWrapper(v8::Isolate::GetCurrent(), buffer->wrapperTypeInfo(), object);
    ASSERT_UNUSED(associatedWrapper, associatedWrapper == object);

    return buffer.get();
}

TestArrayBuffer* V8ArrayBuffer::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8ArrayBuffer::refObject(ScriptWrappable* scriptWrappable)
{
    scriptWrappable->toImpl<TestArrayBuffer>()->ref();
}

void V8ArrayBuffer::derefObject(ScriptWrappable* scriptWrappable)
{
    scriptWrappable->toImpl<TestArrayBuffer>()->deref();
}

} // namespace blink
