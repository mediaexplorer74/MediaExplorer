
// Copyright (c) 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

#ifndef CSSPropertyNames_h
#define CSSPropertyNames_h

#include "core/css/parser/CSSParserMode.h"
#include "wtf/HashFunctions.h"
#include "wtf/HashTraits.h"
#include <string.h>

namespace WTF {
class AtomicString;
class String;
}

namespace blink {

enum CSSPropertyID {
    CSSPropertyInvalid = 0,
    CSSPropertyVariable = 1,
    CSSPropertyColor = 2,
    CSSPropertyDirection = 3,
    CSSPropertyFontFamily = 4,
    CSSPropertyFontKerning = 5,
    CSSPropertyFontSize = 6,
    CSSPropertyFontSizeAdjust = 7,
    CSSPropertyFontStretch = 8,
    CSSPropertyFontStyle = 9,
    CSSPropertyFontVariant = 10,
    CSSPropertyFontVariantLigatures = 11,
    CSSPropertyFontWeight = 12,
    CSSPropertyFontFeatureSettings = 13,
    CSSPropertyWebkitFontSmoothing = 14,
    CSSPropertyWebkitLocale = 15,
    CSSPropertyTextOrientation = 16,
    CSSPropertyWebkitTextOrientation = 17,
    CSSPropertyWritingMode = 18,
    CSSPropertyWebkitWritingMode = 19,
    CSSPropertyTextRendering = 20,
    CSSPropertyZoom = 21,
    CSSPropertyAlignContent = 22,
    CSSPropertyAlignItems = 23,
    CSSPropertyAlignmentBaseline = 24,
    CSSPropertyAlignSelf = 25,
    CSSPropertyAnimationDelay = 26,
    CSSPropertyAnimationDirection = 27,
    CSSPropertyAnimationDuration = 28,
    CSSPropertyAnimationFillMode = 29,
    CSSPropertyAnimationIterationCount = 30,
    CSSPropertyAnimationName = 31,
    CSSPropertyAnimationPlayState = 32,
    CSSPropertyAnimationTimingFunction = 33,
    CSSPropertyBackdropFilter = 34,
    CSSPropertyBackfaceVisibility = 35,
    CSSPropertyBackgroundAttachment = 36,
    CSSPropertyBackgroundBlendMode = 37,
    CSSPropertyBackgroundClip = 38,
    CSSPropertyBackgroundColor = 39,
    CSSPropertyBackgroundImage = 40,
    CSSPropertyBackgroundOrigin = 41,
    CSSPropertyBackgroundPositionX = 42,
    CSSPropertyBackgroundPositionY = 43,
    CSSPropertyBackgroundRepeatX = 44,
    CSSPropertyBackgroundRepeatY = 45,
    CSSPropertyBackgroundSize = 46,
    CSSPropertyBaselineShift = 47,
    CSSPropertyBorderBottomColor = 48,
    CSSPropertyBorderBottomLeftRadius = 49,
    CSSPropertyBorderBottomRightRadius = 50,
    CSSPropertyBorderBottomStyle = 51,
    CSSPropertyBorderBottomWidth = 52,
    CSSPropertyBorderCollapse = 53,
    CSSPropertyBorderImageOutset = 54,
    CSSPropertyBorderImageRepeat = 55,
    CSSPropertyBorderImageSlice = 56,
    CSSPropertyBorderImageSource = 57,
    CSSPropertyBorderImageWidth = 58,
    CSSPropertyBorderLeftColor = 59,
    CSSPropertyBorderLeftStyle = 60,
    CSSPropertyBorderLeftWidth = 61,
    CSSPropertyBorderRightColor = 62,
    CSSPropertyBorderRightStyle = 63,
    CSSPropertyBorderRightWidth = 64,
    CSSPropertyBorderTopColor = 65,
    CSSPropertyBorderTopLeftRadius = 66,
    CSSPropertyBorderTopRightRadius = 67,
    CSSPropertyBorderTopStyle = 68,
    CSSPropertyBorderTopWidth = 69,
    CSSPropertyBottom = 70,
    CSSPropertyBoxShadow = 71,
    CSSPropertyBoxSizing = 72,
    CSSPropertyBufferedRendering = 73,
    CSSPropertyCaptionSide = 74,
    CSSPropertyClear = 75,
    CSSPropertyClip = 76,
    CSSPropertyClipPath = 77,
    CSSPropertyClipRule = 78,
    CSSPropertyColorInterpolation = 79,
    CSSPropertyColorInterpolationFilters = 80,
    CSSPropertyColorRendering = 81,
    CSSPropertyColumnFill = 82,
    CSSPropertyContent = 83,
    CSSPropertyCounterIncrement = 84,
    CSSPropertyCounterReset = 85,
    CSSPropertyCursor = 86,
    CSSPropertyCx = 87,
    CSSPropertyCy = 88,
    CSSPropertyDisplay = 89,
    CSSPropertyDominantBaseline = 90,
    CSSPropertyEmptyCells = 91,
    CSSPropertyFill = 92,
    CSSPropertyFillOpacity = 93,
    CSSPropertyFillRule = 94,
    CSSPropertyFilter = 95,
    CSSPropertyFlexBasis = 96,
    CSSPropertyFlexDirection = 97,
    CSSPropertyFlexGrow = 98,
    CSSPropertyFlexShrink = 99,
    CSSPropertyFlexWrap = 100,
    CSSPropertyFloat = 101,
    CSSPropertyFloodColor = 102,
    CSSPropertyFloodOpacity = 103,
    CSSPropertyGridAutoColumns = 104,
    CSSPropertyGridAutoFlow = 105,
    CSSPropertyGridAutoRows = 106,
    CSSPropertyGridColumnEnd = 107,
    CSSPropertyGridColumnGap = 108,
    CSSPropertyGridColumnStart = 109,
    CSSPropertyGridRowEnd = 110,
    CSSPropertyGridRowGap = 111,
    CSSPropertyGridRowStart = 112,
    CSSPropertyGridTemplateAreas = 113,
    CSSPropertyGridTemplateColumns = 114,
    CSSPropertyGridTemplateRows = 115,
    CSSPropertyHeight = 116,
    CSSPropertyImageRendering = 117,
    CSSPropertyImageOrientation = 118,
    CSSPropertyIsolation = 119,
    CSSPropertyJustifyContent = 120,
    CSSPropertyJustifyItems = 121,
    CSSPropertyJustifySelf = 122,
    CSSPropertyLeft = 123,
    CSSPropertyLetterSpacing = 124,
    CSSPropertyLightingColor = 125,
    CSSPropertyLineHeight = 126,
    CSSPropertyListStyleImage = 127,
    CSSPropertyListStylePosition = 128,
    CSSPropertyListStyleType = 129,
    CSSPropertyMarginBottom = 130,
    CSSPropertyMarginLeft = 131,
    CSSPropertyMarginRight = 132,
    CSSPropertyMarginTop = 133,
    CSSPropertyMarkerEnd = 134,
    CSSPropertyMarkerMid = 135,
    CSSPropertyMarkerStart = 136,
    CSSPropertyMask = 137,
    CSSPropertyMaskSourceType = 138,
    CSSPropertyMaskType = 139,
    CSSPropertyMaxHeight = 140,
    CSSPropertyMaxWidth = 141,
    CSSPropertyMinHeight = 142,
    CSSPropertyMinWidth = 143,
    CSSPropertyMixBlendMode = 144,
    CSSPropertyMotionOffset = 145,
    CSSPropertyMotionPath = 146,
    CSSPropertyMotionRotation = 147,
    CSSPropertyObjectFit = 148,
    CSSPropertyObjectPosition = 149,
    CSSPropertyOpacity = 150,
    CSSPropertyOrder = 151,
    CSSPropertyOrphans = 152,
    CSSPropertyOutlineColor = 153,
    CSSPropertyOutlineOffset = 154,
    CSSPropertyOutlineStyle = 155,
    CSSPropertyOutlineWidth = 156,
    CSSPropertyOverflowWrap = 157,
    CSSPropertyOverflowX = 158,
    CSSPropertyOverflowY = 159,
    CSSPropertyPaddingBottom = 160,
    CSSPropertyPaddingLeft = 161,
    CSSPropertyPaddingRight = 162,
    CSSPropertyPaddingTop = 163,
    CSSPropertyPageBreakAfter = 164,
    CSSPropertyPageBreakBefore = 165,
    CSSPropertyPageBreakInside = 166,
    CSSPropertyPaintOrder = 167,
    CSSPropertyPerspective = 168,
    CSSPropertyPerspectiveOrigin = 169,
    CSSPropertyPointerEvents = 170,
    CSSPropertyPosition = 171,
    CSSPropertyQuotes = 172,
    CSSPropertyResize = 173,
    CSSPropertyRight = 174,
    CSSPropertyR = 175,
    CSSPropertyRx = 176,
    CSSPropertyRy = 177,
    CSSPropertyScrollBehavior = 178,
    CSSPropertyScrollSnapType = 179,
    CSSPropertyScrollSnapPointsX = 180,
    CSSPropertyScrollSnapPointsY = 181,
    CSSPropertyScrollSnapDestination = 182,
    CSSPropertyScrollSnapCoordinate = 183,
    CSSPropertyShapeImageThreshold = 184,
    CSSPropertyShapeMargin = 185,
    CSSPropertyShapeOutside = 186,
    CSSPropertyShapeRendering = 187,
    CSSPropertySize = 188,
    CSSPropertySpeak = 189,
    CSSPropertyStopColor = 190,
    CSSPropertyStopOpacity = 191,
    CSSPropertyStroke = 192,
    CSSPropertyStrokeDasharray = 193,
    CSSPropertyStrokeDashoffset = 194,
    CSSPropertyStrokeLinecap = 195,
    CSSPropertyStrokeLinejoin = 196,
    CSSPropertyStrokeMiterlimit = 197,
    CSSPropertyStrokeOpacity = 198,
    CSSPropertyStrokeWidth = 199,
    CSSPropertyTableLayout = 200,
    CSSPropertyTabSize = 201,
    CSSPropertyTextAlign = 202,
    CSSPropertyTextAlignLast = 203,
    CSSPropertyTextAnchor = 204,
    CSSPropertyTextCombineUpright = 205,
    CSSPropertyTextDecoration = 206,
    CSSPropertyTextDecorationColor = 207,
    CSSPropertyTextDecorationLine = 208,
    CSSPropertyTextDecorationStyle = 209,
    CSSPropertyTextIndent = 210,
    CSSPropertyTextJustify = 211,
    CSSPropertyTextOverflow = 212,
    CSSPropertyTextShadow = 213,
    CSSPropertyTextTransform = 214,
    CSSPropertyTextUnderlinePosition = 215,
    CSSPropertyTop = 216,
    CSSPropertyTouchAction = 217,
    CSSPropertyTransform = 218,
    CSSPropertyTransformOrigin = 219,
    CSSPropertyTransformStyle = 220,
    CSSPropertyTranslate = 221,
    CSSPropertyRotate = 222,
    CSSPropertyScale = 223,
    CSSPropertyTransitionDelay = 224,
    CSSPropertyTransitionDuration = 225,
    CSSPropertyTransitionProperty = 226,
    CSSPropertyTransitionTimingFunction = 227,
    CSSPropertyUnicodeBidi = 228,
    CSSPropertyVectorEffect = 229,
    CSSPropertyVerticalAlign = 230,
    CSSPropertyVisibility = 231,
    CSSPropertyX = 232,
    CSSPropertyY = 233,
    CSSPropertyWebkitAppearance = 234,
    CSSPropertyWebkitAppRegion = 235,
    CSSPropertyWebkitBackgroundClip = 236,
    CSSPropertyWebkitBackgroundComposite = 237,
    CSSPropertyWebkitBackgroundOrigin = 238,
    CSSPropertyWebkitBorderHorizontalSpacing = 239,
    CSSPropertyWebkitBorderImage = 240,
    CSSPropertyWebkitBorderVerticalSpacing = 241,
    CSSPropertyWebkitBoxAlign = 242,
    CSSPropertyWebkitBoxDecorationBreak = 243,
    CSSPropertyWebkitBoxDirection = 244,
    CSSPropertyWebkitBoxFlex = 245,
    CSSPropertyWebkitBoxFlexGroup = 246,
    CSSPropertyWebkitBoxLines = 247,
    CSSPropertyWebkitBoxOrdinalGroup = 248,
    CSSPropertyWebkitBoxOrient = 249,
    CSSPropertyWebkitBoxPack = 250,
    CSSPropertyWebkitBoxReflect = 251,
    CSSPropertyWebkitCaretColor = 252,
    CSSPropertyWebkitClipPath = 253,
    CSSPropertyWebkitColumnBreakAfter = 254,
    CSSPropertyWebkitColumnBreakBefore = 255,
    CSSPropertyWebkitColumnBreakInside = 256,
    CSSPropertyWebkitColumnCount = 257,
    CSSPropertyWebkitColumnGap = 258,
    CSSPropertyWebkitColumnRuleColor = 259,
    CSSPropertyWebkitColumnRuleStyle = 260,
    CSSPropertyWebkitColumnRuleWidth = 261,
    CSSPropertyWebkitColumnSpan = 262,
    CSSPropertyWebkitColumnWidth = 263,
    CSSPropertyWebkitFilter = 264,
    CSSPropertyWebkitHighlight = 265,
    CSSPropertyWebkitHyphenateCharacter = 266,
    CSSPropertyWebkitLineBreak = 267,
    CSSPropertyWebkitLineClamp = 268,
    CSSPropertyWebkitMarginAfterCollapse = 269,
    CSSPropertyWebkitMarginBeforeCollapse = 270,
    CSSPropertyWebkitMarginBottomCollapse = 271,
    CSSPropertyWebkitMarginTopCollapse = 272,
    CSSPropertyWebkitMaskBoxImageOutset = 273,
    CSSPropertyWebkitMaskBoxImageRepeat = 274,
    CSSPropertyWebkitMaskBoxImageSlice = 275,
    CSSPropertyWebkitMaskBoxImageSource = 276,
    CSSPropertyWebkitMaskBoxImageWidth = 277,
    CSSPropertyWebkitMaskClip = 278,
    CSSPropertyWebkitMaskComposite = 279,
    CSSPropertyWebkitMaskImage = 280,
    CSSPropertyWebkitMaskOrigin = 281,
    CSSPropertyWebkitMaskPositionX = 282,
    CSSPropertyWebkitMaskPositionY = 283,
    CSSPropertyWebkitMaskRepeatX = 284,
    CSSPropertyWebkitMaskRepeatY = 285,
    CSSPropertyWebkitMaskSize = 286,
    CSSPropertyWebkitPerspectiveOriginX = 287,
    CSSPropertyWebkitPerspectiveOriginY = 288,
    CSSPropertyWebkitPrintColorAdjust = 289,
    CSSPropertyWebkitRtlOrdering = 290,
    CSSPropertyWebkitRubyPosition = 291,
    CSSPropertyWebkitTapHighlightColor = 292,
    CSSPropertyWebkitTextCombine = 293,
    CSSPropertyWebkitTextEmphasisColor = 294,
    CSSPropertyWebkitTextEmphasisPosition = 295,
    CSSPropertyWebkitTextEmphasisStyle = 296,
    CSSPropertyWebkitTextFillColor = 297,
    CSSPropertyWebkitTextSecurity = 298,
    CSSPropertyWebkitTextStrokeColor = 299,
    CSSPropertyWebkitTextStrokeWidth = 300,
    CSSPropertyWebkitTransformOriginX = 301,
    CSSPropertyWebkitTransformOriginY = 302,
    CSSPropertyWebkitTransformOriginZ = 303,
    CSSPropertyWebkitUserDrag = 304,
    CSSPropertyWebkitUserModify = 305,
    CSSPropertyWebkitUserSelect = 306,
    CSSPropertyBbRubberbandable = 307,
    CSSPropertyWhiteSpace = 308,
    CSSPropertyWidows = 309,
    CSSPropertyWidth = 310,
    CSSPropertyWillChange = 311,
    CSSPropertyWordBreak = 312,
    CSSPropertyWordSpacing = 313,
    CSSPropertyWordWrap = 314,
    CSSPropertyZIndex = 315,
    CSSPropertyWebkitBorderEndColor = 316,
    CSSPropertyWebkitBorderEndStyle = 317,
    CSSPropertyWebkitBorderEndWidth = 318,
    CSSPropertyWebkitBorderStartColor = 319,
    CSSPropertyWebkitBorderStartStyle = 320,
    CSSPropertyWebkitBorderStartWidth = 321,
    CSSPropertyWebkitBorderBeforeColor = 322,
    CSSPropertyWebkitBorderBeforeStyle = 323,
    CSSPropertyWebkitBorderBeforeWidth = 324,
    CSSPropertyWebkitBorderAfterColor = 325,
    CSSPropertyWebkitBorderAfterStyle = 326,
    CSSPropertyWebkitBorderAfterWidth = 327,
    CSSPropertyWebkitMarginEnd = 328,
    CSSPropertyWebkitMarginStart = 329,
    CSSPropertyWebkitMarginBefore = 330,
    CSSPropertyWebkitMarginAfter = 331,
    CSSPropertyWebkitPaddingEnd = 332,
    CSSPropertyWebkitPaddingStart = 333,
    CSSPropertyWebkitPaddingBefore = 334,
    CSSPropertyWebkitPaddingAfter = 335,
    CSSPropertyWebkitLogicalWidth = 336,
    CSSPropertyWebkitLogicalHeight = 337,
    CSSPropertyWebkitMinLogicalWidth = 338,
    CSSPropertyWebkitMinLogicalHeight = 339,
    CSSPropertyWebkitMaxLogicalWidth = 340,
    CSSPropertyWebkitMaxLogicalHeight = 341,
    CSSPropertyAll = 342,
    CSSPropertyMaxZoom = 343,
    CSSPropertyMinZoom = 344,
    CSSPropertyOrientation = 345,
    CSSPropertyPage = 346,
    CSSPropertySrc = 347,
    CSSPropertyUnicodeRange = 348,
    CSSPropertyUserZoom = 349,
    CSSPropertyWebkitFontSizeDelta = 350,
    CSSPropertyWebkitTextDecorationsInEffect = 351,
    CSSPropertyAnimation = 352,
    CSSPropertyBackground = 353,
    CSSPropertyBackgroundPosition = 354,
    CSSPropertyBackgroundRepeat = 355,
    CSSPropertyBorder = 356,
    CSSPropertyBorderBottom = 357,
    CSSPropertyBorderColor = 358,
    CSSPropertyBorderImage = 359,
    CSSPropertyBorderLeft = 360,
    CSSPropertyBorderRadius = 361,
    CSSPropertyBorderRight = 362,
    CSSPropertyBorderSpacing = 363,
    CSSPropertyBorderStyle = 364,
    CSSPropertyBorderTop = 365,
    CSSPropertyBorderWidth = 366,
    CSSPropertyFlex = 367,
    CSSPropertyFlexFlow = 368,
    CSSPropertyFont = 369,
    CSSPropertyGrid = 370,
    CSSPropertyGridArea = 371,
    CSSPropertyGridColumn = 372,
    CSSPropertyGridGap = 373,
    CSSPropertyGridRow = 374,
    CSSPropertyGridTemplate = 375,
    CSSPropertyListStyle = 376,
    CSSPropertyMargin = 377,
    CSSPropertyMarker = 378,
    CSSPropertyMotion = 379,
    CSSPropertyOutline = 380,
    CSSPropertyOverflow = 381,
    CSSPropertyPadding = 382,
    CSSPropertyTransition = 383,
    CSSPropertyWebkitBorderAfter = 384,
    CSSPropertyWebkitBorderBefore = 385,
    CSSPropertyWebkitBorderEnd = 386,
    CSSPropertyWebkitBorderStart = 387,
    CSSPropertyWebkitColumnRule = 388,
    CSSPropertyWebkitColumns = 389,
    CSSPropertyWebkitMarginCollapse = 390,
    CSSPropertyWebkitMask = 391,
    CSSPropertyWebkitMaskBoxImage = 392,
    CSSPropertyWebkitMaskPosition = 393,
    CSSPropertyWebkitMaskRepeat = 394,
    CSSPropertyWebkitTextEmphasis = 395,
    CSSPropertyWebkitTextStroke = 396,
    CSSPropertyAliasEpubCaptionSide = 586,
    CSSPropertyAliasEpubTextCombine = 805,
    CSSPropertyAliasEpubTextEmphasis = 907,
    CSSPropertyAliasEpubTextEmphasisColor = 806,
    CSSPropertyAliasEpubTextEmphasisStyle = 808,
    CSSPropertyAliasEpubTextOrientation = 529,
    CSSPropertyAliasEpubTextTransform = 726,
    CSSPropertyAliasEpubWordBreak = 824,
    CSSPropertyAliasEpubWritingMode = 531,
    CSSPropertyAliasWebkitAlignContent = 534,
    CSSPropertyAliasWebkitAlignItems = 535,
    CSSPropertyAliasWebkitAlignSelf = 537,
    CSSPropertyAliasWebkitAnimation = 864,
    CSSPropertyAliasWebkitAnimationDelay = 538,
    CSSPropertyAliasWebkitAnimationDirection = 539,
    CSSPropertyAliasWebkitAnimationDuration = 540,
    CSSPropertyAliasWebkitAnimationFillMode = 541,
    CSSPropertyAliasWebkitAnimationIterationCount = 542,
    CSSPropertyAliasWebkitAnimationName = 543,
    CSSPropertyAliasWebkitAnimationPlayState = 544,
    CSSPropertyAliasWebkitAnimationTimingFunction = 545,
    CSSPropertyAliasWebkitBackfaceVisibility = 547,
    CSSPropertyAliasWebkitBackgroundSize = 558,
    CSSPropertyAliasWebkitBorderBottomLeftRadius = 561,
    CSSPropertyAliasWebkitBorderBottomRightRadius = 562,
    CSSPropertyAliasWebkitBorderRadius = 873,
    CSSPropertyAliasWebkitBorderTopLeftRadius = 578,
    CSSPropertyAliasWebkitBorderTopRightRadius = 579,
    CSSPropertyAliasWebkitBoxShadow = 583,
    CSSPropertyAliasWebkitBoxSizing = 584,
    CSSPropertyAliasWebkitFlex = 879,
    CSSPropertyAliasWebkitFlexBasis = 608,
    CSSPropertyAliasWebkitFlexDirection = 609,
    CSSPropertyAliasWebkitFlexFlow = 880,
    CSSPropertyAliasWebkitFlexGrow = 610,
    CSSPropertyAliasWebkitFlexShrink = 611,
    CSSPropertyAliasWebkitFlexWrap = 612,
    CSSPropertyAliasWebkitFontFeatureSettings = 525,
    CSSPropertyAliasWebkitJustifyContent = 632,
    CSSPropertyAliasWebkitOpacity = 662,
    CSSPropertyAliasWebkitOrder = 663,
    CSSPropertyAliasWebkitPerspective = 680,
    CSSPropertyAliasWebkitPerspectiveOrigin = 681,
    CSSPropertyAliasWebkitShapeImageThreshold = 696,
    CSSPropertyAliasWebkitShapeMargin = 697,
    CSSPropertyAliasWebkitShapeOutside = 698,
    CSSPropertyAliasWebkitTransform = 730,
    CSSPropertyAliasWebkitTransformOrigin = 731,
    CSSPropertyAliasWebkitTransformStyle = 732,
    CSSPropertyAliasWebkitTransition = 895,
    CSSPropertyAliasWebkitTransitionDelay = 736,
    CSSPropertyAliasWebkitTransitionDuration = 737,
    CSSPropertyAliasWebkitTransitionProperty = 738,
    CSSPropertyAliasWebkitTransitionTimingFunction = 739,

	CSSPropertyWebkitFontFeatureSettings = CSSPropertyAliasWebkitFontFeatureSettings,
};

const int firstCSSProperty = 2;
const int numCSSProperties = 395;
const int lastCSSProperty = 396;
const int lastUnresolvedCSSProperty = 907;
const size_t maxCSSPropertyNameLength = 40;

const char* getPropertyName(CSSPropertyID);
const WTF::AtomicString& getPropertyNameAtomicString(CSSPropertyID);
WTF::String getPropertyNameString(CSSPropertyID);
WTF::String getJSPropertyName(CSSPropertyID);

inline CSSPropertyID convertToCSSPropertyID(int value)
{
    ASSERT((value >= firstCSSProperty && value <= lastCSSProperty) || value == CSSPropertyInvalid || value == CSSPropertyVariable);
    return static_cast<CSSPropertyID>(value);
}

inline CSSPropertyID resolveCSSPropertyID(CSSPropertyID id)
{
    return convertToCSSPropertyID(id & ~512);
}

inline bool isPropertyAlias(CSSPropertyID id) { return id & 512; }

CSSPropertyID unresolvedCSSPropertyID(const WTF::String&);

CSSPropertyID cssPropertyID(const WTF::String&);

} // namespace blink

namespace WTF {
template<> struct DefaultHash<blink::CSSPropertyID> { typedef IntHash<unsigned> Hash; };
template<> struct HashTraits<blink::CSSPropertyID> : GenericHashTraits<blink::CSSPropertyID> {
    static const bool emptyValueIsZero = true;
    static void constructDeletedValue(blink::CSSPropertyID& slot, bool) { slot = static_cast<blink::CSSPropertyID>(blink::lastUnresolvedCSSProperty + 1); }
    static bool isDeletedValue(blink::CSSPropertyID value) { return value == (blink::lastUnresolvedCSSProperty + 1); }
};
}

#endif // CSSPropertyNames_h