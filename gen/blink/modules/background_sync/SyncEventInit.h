// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#ifndef SyncEventInit_h
#define SyncEventInit_h

#include "modules/ModulesExport.h"
#include "modules/background_sync/SyncRegistration.h"
#include "modules/serviceworkers/ExtendableEventInit.h"
#include "platform/heap/Handle.h"

namespace blink {

class MODULES_EXPORT SyncEventInit : public ExtendableEventInit {
    ALLOW_ONLY_INLINE_ALLOCATION();
public:
    SyncEventInit();

    bool hasRegistration() const { return m_registration; }
    SyncRegistration* registration() const { return m_registration; }
    void setRegistration(SyncRegistration* value) { m_registration = value; }

    DECLARE_VIRTUAL_TRACE();

private:
    Member<SyncRegistration> m_registration;

    friend class V8SyncEventInit;
};

} // namespace blink

#endif // SyncEventInit_h
