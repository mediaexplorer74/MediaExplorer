// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#ifndef MediaKeyMessageEventInit_h
#define MediaKeyMessageEventInit_h

#include "core/dom/DOMArrayBuffer.h"
#include "core/events/EventInit.h"
#include "modules/ModulesExport.h"
#include "platform/heap/Handle.h"
#include "wtf/text/WTFString.h"

namespace blink {

class MODULES_EXPORT MediaKeyMessageEventInit : public EventInit {
    ALLOW_ONLY_INLINE_ALLOCATION();
public:
    MediaKeyMessageEventInit();

    bool hasMessage() const { return m_message; }
    PassRefPtr<DOMArrayBuffer> message() const { return m_message; }
    void setMessage(PassRefPtr<DOMArrayBuffer> value) { m_message = value; }

    bool hasMessageType() const { return !m_messageType.isNull(); }
    String messageType() const { return m_messageType; }
    void setMessageType(String value) { m_messageType = value; }

    DECLARE_VIRTUAL_TRACE();

private:
    RefPtr<DOMArrayBuffer> m_message;
    String m_messageType;

    friend class V8MediaKeyMessageEventInit;
};

} // namespace blink

#endif // MediaKeyMessageEventInit_h
