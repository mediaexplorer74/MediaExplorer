// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8CanvasContextCreationAttributes.h"

#include "bindings/core/v8/ExceptionState.h"

namespace blink {

void V8CanvasContextCreationAttributes::toImpl(v8::Isolate* isolate, v8::Local<v8::Value> v8Value, CanvasContextCreationAttributes& impl, ExceptionState& exceptionState)
{
    if (isUndefinedOrNull(v8Value))
        return;
    if (!v8Value->IsObject()) {
        // Do nothing.
        return;
    }

    v8::TryCatch block(isolate);
    v8::Local<v8::Object> v8Object;
    if (!v8Call(v8Value->ToObject(isolate->GetCurrentContext()), v8Object, block)) {
        exceptionState.rethrowV8Exception(block.Exception());
        return;
    }
    {
        v8::Local<v8::Value> alphaValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "alpha")).ToLocal(&alphaValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (alphaValue.IsEmpty() || alphaValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool alpha = toBoolean(isolate, alphaValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setAlpha(alpha);
        }
    }

    {
        v8::Local<v8::Value> antialiasValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "antialias")).ToLocal(&antialiasValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (antialiasValue.IsEmpty() || antialiasValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool antialias = toBoolean(isolate, antialiasValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setAntialias(antialias);
        }
    }

    {
        v8::Local<v8::Value> depthValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "depth")).ToLocal(&depthValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (depthValue.IsEmpty() || depthValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool depth = toBoolean(isolate, depthValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setDepth(depth);
        }
    }

    {
        v8::Local<v8::Value> failIfMajorPerformanceCaveatValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "failIfMajorPerformanceCaveat")).ToLocal(&failIfMajorPerformanceCaveatValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (failIfMajorPerformanceCaveatValue.IsEmpty() || failIfMajorPerformanceCaveatValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool failIfMajorPerformanceCaveat = toBoolean(isolate, failIfMajorPerformanceCaveatValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setFailIfMajorPerformanceCaveat(failIfMajorPerformanceCaveat);
        }
    }

    {
        v8::Local<v8::Value> premultipliedAlphaValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "premultipliedAlpha")).ToLocal(&premultipliedAlphaValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (premultipliedAlphaValue.IsEmpty() || premultipliedAlphaValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool premultipliedAlpha = toBoolean(isolate, premultipliedAlphaValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setPremultipliedAlpha(premultipliedAlpha);
        }
    }

    {
        v8::Local<v8::Value> preserveDrawingBufferValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "preserveDrawingBuffer")).ToLocal(&preserveDrawingBufferValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (preserveDrawingBufferValue.IsEmpty() || preserveDrawingBufferValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool preserveDrawingBuffer = toBoolean(isolate, preserveDrawingBufferValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setPreserveDrawingBuffer(preserveDrawingBuffer);
        }
    }

    {
        v8::Local<v8::Value> stencilValue;
        if (!v8Object->Get(isolate->GetCurrentContext(), v8String(isolate, "stencil")).ToLocal(&stencilValue)) {
            exceptionState.rethrowV8Exception(block.Exception());
            return;
        }
        if (stencilValue.IsEmpty() || stencilValue->IsUndefined()) {
            // Do nothing.
        } else {
            bool stencil = toBoolean(isolate, stencilValue, exceptionState);
            if (exceptionState.hadException())
                return;
            impl.setStencil(stencil);
        }
    }

}

v8::Local<v8::Value> toV8(const CanvasContextCreationAttributes& impl, v8::Local<v8::Object> creationContext, v8::Isolate* isolate)
{
    v8::Local<v8::Object> v8Object = v8::Object::New(isolate);
    if (!toV8CanvasContextCreationAttributes(impl, v8Object, creationContext, isolate))
        return v8::Local<v8::Value>();
    return v8Object;
}

bool toV8CanvasContextCreationAttributes(const CanvasContextCreationAttributes& impl, v8::Local<v8::Object> dictionary, v8::Local<v8::Object> creationContext, v8::Isolate* isolate)
{
    if (impl.hasAlpha()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "alpha"), v8Boolean(impl.alpha(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "alpha"), v8Boolean(true, isolate))))
            return false;
    }

    if (impl.hasAntialias()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "antialias"), v8Boolean(impl.antialias(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "antialias"), v8Boolean(true, isolate))))
            return false;
    }

    if (impl.hasDepth()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "depth"), v8Boolean(impl.depth(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "depth"), v8Boolean(true, isolate))))
            return false;
    }

    if (impl.hasFailIfMajorPerformanceCaveat()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "failIfMajorPerformanceCaveat"), v8Boolean(impl.failIfMajorPerformanceCaveat(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "failIfMajorPerformanceCaveat"), v8Boolean(false, isolate))))
            return false;
    }

    if (impl.hasPremultipliedAlpha()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "premultipliedAlpha"), v8Boolean(impl.premultipliedAlpha(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "premultipliedAlpha"), v8Boolean(true, isolate))))
            return false;
    }

    if (impl.hasPreserveDrawingBuffer()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "preserveDrawingBuffer"), v8Boolean(impl.preserveDrawingBuffer(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "preserveDrawingBuffer"), v8Boolean(false, isolate))))
            return false;
    }

    if (impl.hasStencil()) {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "stencil"), v8Boolean(impl.stencil(), isolate))))
            return false;
    } else {
        if (!v8CallBoolean(dictionary->CreateDataProperty(isolate->GetCurrentContext(), v8String(isolate, "stencil"), v8Boolean(false, isolate))))
            return false;
    }

    return true;
}

CanvasContextCreationAttributes NativeValueTraits<CanvasContextCreationAttributes>::nativeValue(v8::Isolate* isolate, v8::Local<v8::Value> value, ExceptionState& exceptionState)
{
    CanvasContextCreationAttributes impl;
    V8CanvasContextCreationAttributes::toImpl(isolate, value, impl, exceptionState);
    return impl;
}

} // namespace blink
