// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8EXTTextureFilterAnisotropic.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8GCController.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "core/dom/Element.h"
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
const WrapperTypeInfo V8EXTTextureFilterAnisotropic::wrapperTypeInfo = { gin::kEmbedderBlink, V8EXTTextureFilterAnisotropic::domTemplate, V8EXTTextureFilterAnisotropic::refObject, V8EXTTextureFilterAnisotropic::derefObject, V8EXTTextureFilterAnisotropic::trace, 0, V8EXTTextureFilterAnisotropic::visitDOMWrapper, V8EXTTextureFilterAnisotropic::preparePrototypeObject, V8EXTTextureFilterAnisotropic::installConditionallyEnabledProperties, "EXTTextureFilterAnisotropic", 0, WrapperTypeInfo::WrapperTypeObjectPrototype, WrapperTypeInfo::ObjectClassId, WrapperTypeInfo::NotInheritFromEventTarget, WrapperTypeInfo::Dependent, WrapperTypeInfo::WillBeGarbageCollectedObject };
#if defined(COMPONENT_BUILD) && defined(WIN32) && COMPILER(CLANG)
#pragma clang diagnostic pop
#endif

// This static member must be declared by DEFINE_WRAPPERTYPEINFO in EXTTextureFilterAnisotropic.h.
// For details, see the comment of DEFINE_WRAPPERTYPEINFO in
// bindings/core/v8/ScriptWrappable.h.
const WrapperTypeInfo& EXTTextureFilterAnisotropic::s_wrapperTypeInfo = V8EXTTextureFilterAnisotropic::wrapperTypeInfo;

namespace EXTTextureFilterAnisotropicV8Internal {

} // namespace EXTTextureFilterAnisotropicV8Internal

void V8EXTTextureFilterAnisotropic::visitDOMWrapper(v8::Isolate* isolate, ScriptWrappable* scriptWrappable, const v8::Persistent<v8::Object>& wrapper)
{
    EXTTextureFilterAnisotropic* impl = scriptWrappable->toImpl<EXTTextureFilterAnisotropic>();
    // The canvas() method may return a reference or a pointer.
    if (Node* owner = WTF::getPtr(impl->canvas())) {
        Node* root = V8GCController::opaqueRootForGC(isolate, owner);
        isolate->SetReferenceFromGroup(v8::UniqueId(reinterpret_cast<intptr_t>(root)), wrapper);
        return;
    }
}

static void installV8EXTTextureFilterAnisotropicTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    functionTemplate->ReadOnlyPrototype();

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "EXTTextureFilterAnisotropic", v8::Local<v8::FunctionTemplate>(), V8EXTTextureFilterAnisotropic::internalFieldCount,
        0, 0,
        0, 0,
        0, 0);
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
    static const V8DOMConfiguration::ConstantConfiguration V8EXTTextureFilterAnisotropicConstants[] = {
        {"TEXTURE_MAX_ANISOTROPY_EXT", 0x84FE, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedLong},
        {"MAX_TEXTURE_MAX_ANISOTROPY_EXT", 0x84FF, 0, 0, V8DOMConfiguration::ConstantTypeUnsignedLong},
    };
    V8DOMConfiguration::installConstants(isolate, functionTemplate, prototypeTemplate, V8EXTTextureFilterAnisotropicConstants, WTF_ARRAY_LENGTH(V8EXTTextureFilterAnisotropicConstants));

    // Custom toString template
    functionTemplate->Set(v8AtomicString(isolate, "toString"), V8PerIsolateData::from(isolate)->toStringTemplate());
}

v8::Local<v8::FunctionTemplate> V8EXTTextureFilterAnisotropic::domTemplate(v8::Isolate* isolate)
{
    return V8DOMConfiguration::domClassTemplate(isolate, const_cast<WrapperTypeInfo*>(&wrapperTypeInfo), installV8EXTTextureFilterAnisotropicTemplate);
}

bool V8EXTTextureFilterAnisotropic::hasInstance(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->hasInstance(&wrapperTypeInfo, v8Value);
}

v8::Local<v8::Object> V8EXTTextureFilterAnisotropic::findInstanceInPrototypeChain(v8::Local<v8::Value> v8Value, v8::Isolate* isolate)
{
    return V8PerIsolateData::from(isolate)->findInstanceInPrototypeChain(&wrapperTypeInfo, v8Value);
}

EXTTextureFilterAnisotropic* V8EXTTextureFilterAnisotropic::toImplWithTypeCheck(v8::Isolate* isolate, v8::Local<v8::Value> value)
{
    return hasInstance(value, isolate) ? toImpl(v8::Local<v8::Object>::Cast(value)) : 0;
}

void V8EXTTextureFilterAnisotropic::refObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<EXTTextureFilterAnisotropic>()->ref();
#endif
}

void V8EXTTextureFilterAnisotropic::derefObject(ScriptWrappable* scriptWrappable)
{
#if !ENABLE(OILPAN)
    scriptWrappable->toImpl<EXTTextureFilterAnisotropic>()->deref();
#endif
}

} // namespace blink
