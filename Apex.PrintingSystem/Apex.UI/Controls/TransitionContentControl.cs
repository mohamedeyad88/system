using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Apex.UI.Controls
{
    /// <summary>
    /// A ContentControl that plays a smooth fade + slide-up animation
    /// every time its Content changes (i.e., on every navigation).
    /// </summary>
    public class TransitionContentControl : ContentControl
    {
        private readonly TranslateTransform _translate = new();

        public TransitionContentControl()
        {
            RenderTransform = _translate;
            RenderTransformOrigin = new Point(0.5, 0.5);
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            if (newContent is null) return;
            PlayEnterAnimation();
        }

        private void PlayEnterAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(230));

            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });

            _translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, dur) { EasingFunction = ease });
        }
    }
}
