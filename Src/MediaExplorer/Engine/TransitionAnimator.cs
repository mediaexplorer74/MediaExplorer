using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Lightweight CSS transition → XAML animation bridge.
    /// Phase C.6 / Session 3.26.
    ///
    /// Translates a parsed `transition` shorthand (stored on CssComputed) into
    /// a XAML Storyboard that animates Opacity, BackgroundColor, or a transform
    /// child. Used by DomBasicRenderer when an element with both a :hover
    /// override and a transition is hovered/tapped.
    ///
    /// Scope (matches Plan_04 §C.6 "good enough" approximation):
    /// - <c>opacity</c> → DoubleAnimation on <c>UIElement.Opacity</c>
    /// - <c>background-color</c> (or <c>background</c>) → ColorAnimation on
    ///   <c>(Background).(SolidColorBrush.Color)</c>
    /// - <c>transform</c> → DoubleAnimation on the first matching child
    ///   transform of <c>RenderTransform</c>: scaleX/scaleY on
    ///   <c>ScaleTransform</c>, angle on <c>RotateTransform</c>, x/y on
    ///   <c>TranslateTransform</c>. The animation target is chosen from the
    ///   hover value's transform function name.
    /// - <c>all</c> → currently treated as <c>transform</c> (most common case)
    ///
    /// Easing functions map 1:1 onto XAML's <see cref="EasingFunctionBase"/>
    /// (ease → CubicEase EaseInOut, linear → null, ease-in/out → CubicEase).
    /// </summary>
    public static class TransitionAnimator
    {
        public static void AnimateOpacity(FrameworkElement fe, double to, double durationMs, double delayMs, string timing)
        {
            if (fe == null || durationMs <= 0) return;
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                EasingFunction = BuildEasing(timing),
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(anim, fe);
            Storyboard.SetTargetProperty(anim, "Opacity");
            sb.Children.Add(anim);
            TryBegin(sb);
        }

        public static void AnimateBackgroundColor(FrameworkElement fe, Color to, double durationMs, double delayMs, string timing)
        {
            if (fe == null || durationMs <= 0) return;
            // The animation target is a property path because Background is a Brush.
            // XAML requires the brush to already be a SolidColorBrush (which it
            // is — RendererStyles always uses SolidColorBrush for solid colors).
            var anim = new ColorAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                EasingFunction = BuildEasing(timing),
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(anim, fe);
            Storyboard.SetTargetProperty(anim, "(Background).(SolidColorBrush.Color)");
            sb.Children.Add(anim);
            TryBegin(sb);
        }

        public static void AnimateTransformScale(FrameworkElement fe, double scaleX, double scaleY, double durationMs, double delayMs, string timing)
        {
            if (fe == null || fe.RenderTransform == null || durationMs <= 0) return;

            // Make sure there's a ScaleTransform at a known index. RendererStyles
            // always sets RenderTransform to a TransformGroup with children in
            // declaration order: translate, scale, rotate, skew. If the element
            // had only a scale token, ScaleTransform is at index 0.
            // We use a path that's tolerant of missing transforms.
            var group = fe.RenderTransform as TransformGroup;
            ScaleTransform scaleT = FindTransform<ScaleTransform>(group);
            if (scaleT == null) return; // no scale to animate

            var sb = new Storyboard();

            if (scaleX != scaleT.ScaleX)
            {
                var animX = new DoubleAnimation
                {
                    To = scaleX,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                    EasingFunction = BuildEasing(timing),
                };
                Storyboard.SetTarget(animX, fe);
                Storyboard.SetTargetProperty(animX, "(RenderTransform).(TransformGroup.Children)[" + IndexOfTransform(group, scaleT) + "].(ScaleTransform.ScaleX)");
                sb.Children.Add(animX);
            }
            if (scaleY != scaleT.ScaleY)
            {
                var animY = new DoubleAnimation
                {
                    To = scaleY,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                    EasingFunction = BuildEasing(timing),
                };
                Storyboard.SetTarget(animY, fe);
                Storyboard.SetTargetProperty(animY, "(RenderTransform).(TransformGroup.Children)[" + IndexOfTransform(group, scaleT) + "].(ScaleTransform.ScaleY)");
                sb.Children.Add(animY);
            }
            TryBegin(sb);
        }

        public static void AnimateTransformRotate(FrameworkElement fe, double angleDeg, double durationMs, double delayMs, string timing)
        {
            if (fe == null || fe.RenderTransform == null || durationMs <= 0) return;
            var group = fe.RenderTransform as TransformGroup;
            var rot = FindTransform<RotateTransform>(group);
            if (rot == null) return;
            var anim = new DoubleAnimation
            {
                To = angleDeg,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                EasingFunction = BuildEasing(timing),
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(anim, fe);
            Storyboard.SetTargetProperty(anim, "(RenderTransform).(TransformGroup.Children)[" + IndexOfTransform(group, rot) + "].(RotateTransform.Angle)");
            sb.Children.Add(anim);
            TryBegin(sb);
        }

        public static void AnimateTransformTranslate(FrameworkElement fe, double dx, double dy, double durationMs, double delayMs, string timing)
        {
            if (fe == null || fe.RenderTransform == null || durationMs <= 0) return;
            var group = fe.RenderTransform as TransformGroup;
            var t = FindTransform<TranslateTransform>(group);
            if (t == null) return;
            var sb = new Storyboard();
            if (dx != t.X)
            {
                var animX = new DoubleAnimation
                {
                    To = dx,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                    EasingFunction = BuildEasing(timing),
                };
                Storyboard.SetTarget(animX, fe);
                Storyboard.SetTargetProperty(animX, "(RenderTransform).(TransformGroup.Children)[" + IndexOfTransform(group, t) + "].(TranslateTransform.X)");
                sb.Children.Add(animX);
            }
            if (dy != t.Y)
            {
                var animY = new DoubleAnimation
                {
                    To = dy,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
                    EasingFunction = BuildEasing(timing),
                };
                Storyboard.SetTarget(animY, fe);
                Storyboard.SetTargetProperty(animY, "(RenderTransform).(TransformGroup.Children)[" + IndexOfTransform(group, t) + "].(TranslateTransform.Y)");
                sb.Children.Add(animY);
            }
            TryBegin(sb);
        }

        // ----- helpers -----

        private static T FindTransform<T>(TransformGroup group) where T : Transform
        {
            if (group == null || group.Children == null) return null;
            for (int i = 0; i < group.Children.Count; i++)
            {
                if (group.Children[i] is T t) return t;
            }
            return null;
        }

        private static int IndexOfTransform(TransformGroup group, Transform target)
        {
            if (group == null || group.Children == null || target == null) return -1;
            for (int i = 0; i < group.Children.Count; i++)
            {
                if (ReferenceEquals(group.Children[i], target)) return i;
            }
            return -1;
        }

        private static EasingFunctionBase BuildEasing(string timing)
        {
            if (string.IsNullOrEmpty(timing)) return null;
            var t = timing.ToLowerInvariant();
            if (t == "linear") return null;
            if (t == "ease-in") return new CubicEase { EasingMode = EasingMode.EaseIn };
            if (t == "ease-out") return new CubicEase { EasingMode = EasingMode.EaseOut };
            if (t == "ease-in-out") return new CubicEase { EasingMode = EasingMode.EaseInOut };
            // default "ease" → EaseInOut (closest to the CSS spec curve)
            return new CubicEase { EasingMode = EasingMode.EaseInOut };
        }

        private static void TryBegin(Storyboard sb)
        {
            try
            {
                if (sb.Children.Count > 0) sb.Begin();
            }
            catch { /* swallow — animation is best-effort */ }
        }
    }
}
