// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// This file has been auto-generated by code_generator_v8.py. DO NOT MODIFY!

#include "config.h"
#include "KeyframeEffectOptions.h"


namespace blink {

KeyframeEffectOptions::KeyframeEffectOptions()
{
    setDelay(0);
    setDirection(String("normal"));
    setDuration(UnrestrictedDoubleOrString::fromString(String("auto")));
    setEasing(String("linear"));
    setEndDelay(0);
    setFill(String("auto"));
    setIterationStart(0);
    setIterations(1);
    setPlaybackRate(1);
}

DEFINE_TRACE(KeyframeEffectOptions)
{
    visitor->trace(m_duration);
}

} // namespace blink
