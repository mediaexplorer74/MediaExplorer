// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "V8DataTransferItemPartial.h"

#include "bindings/core/v8/ExceptionState.h"
#include "bindings/core/v8/V8DOMConfiguration.h"
#include "bindings/core/v8/V8DataTransferItem.h"
#include "bindings/core/v8/V8ObjectConstructor.h"
#include "bindings/modules/v8/V8Entry.h"
#include "core/dom/ContextFeatures.h"
#include "core/dom/Document.h"
#include "modules/filesystem/DataTransferItemFileSystem.h"
#include "platform/RuntimeEnabledFeatures.h"
#include "platform/TraceEvent.h"
#include "wtf/GetPtr.h"
#include "wtf/RefPtr.h"

namespace blink {

namespace DataTransferItemPartialV8Internal {

static void webkitGetAsEntryMethod(const v8::FunctionCallbackInfo<v8::Value>& info)
{
//     DataTransferItem* impl = V8DataTransferItem::toImpl(info.Holder());
//     ExecutionContext* executionContext = currentExecutionContext(info.GetIsolate());
//     v8SetReturnValue(info, DataTransferItemFileSystem::webkitGetAsEntry(executionContext, *impl)); // weolar:暂时不实现这接口
}

static void webkitGetAsEntryMethodCallback(const v8::FunctionCallbackInfo<v8::Value>& info)
{
    TRACE_EVENT_SET_SAMPLING_STATE("blink", "DOMMethod");
    DataTransferItemPartialV8Internal::webkitGetAsEntryMethod(info);
    TRACE_EVENT_SET_SAMPLING_STATE("v8", "V8Execution");
}

} // namespace DataTransferItemPartialV8Internal

static const V8DOMConfiguration::MethodConfiguration V8DataTransferItemMethods[] = {
    {"webkitGetAsEntry", DataTransferItemPartialV8Internal::webkitGetAsEntryMethodCallback, 0, 0, V8DOMConfiguration::ExposedToAllScripts},
};

void V8DataTransferItemPartial::installV8DataTransferItemTemplate(v8::Local<v8::FunctionTemplate> functionTemplate, v8::Isolate* isolate)
{
    V8DataTransferItem::installV8DataTransferItemTemplate(functionTemplate, isolate);

    v8::Local<v8::Signature> defaultSignature;
    defaultSignature = V8DOMConfiguration::installDOMClassTemplate(isolate, functionTemplate, "DataTransferItem", v8::Local<v8::FunctionTemplate>(), V8DataTransferItem::internalFieldCount,
        0, 0,
        0, 0,
        V8DataTransferItemMethods, WTF_ARRAY_LENGTH(V8DataTransferItemMethods));
    v8::Local<v8::ObjectTemplate> instanceTemplate = functionTemplate->InstanceTemplate();
    ALLOW_UNUSED_LOCAL(instanceTemplate);
    v8::Local<v8::ObjectTemplate> prototypeTemplate = functionTemplate->PrototypeTemplate();
    ALLOW_UNUSED_LOCAL(prototypeTemplate);
}

void V8DataTransferItemPartial::preparePrototypeObject(v8::Isolate* isolate, v8::Local<v8::Object> prototypeObject, v8::Local<v8::FunctionTemplate> interfaceTemplate)
{
    V8DataTransferItem::preparePrototypeObject(isolate, prototypeObject, interfaceTemplate);
}

void V8DataTransferItemPartial::initialize()
{
    // Should be invoked from initModules.
    V8DataTransferItem::updateWrapperTypeInfo(
        &V8DataTransferItemPartial::installV8DataTransferItemTemplate,
        &V8DataTransferItemPartial::preparePrototypeObject);
}

} // namespace blink
