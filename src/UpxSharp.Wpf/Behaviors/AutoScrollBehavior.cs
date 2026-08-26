using System.Windows;
using System.Windows.Controls;

namespace UpxSharp.Wpf.Behaviors
{
    public static class AutoScrollBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(AutoScrollBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);
        public static bool GetIsEnabled(DependencyObject d)         => (bool)d.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer sv) return;
            if ((bool)e.NewValue) sv.ScrollChanged += OnScrollChanged;
            else                  sv.ScrollChanged -= OnScrollChanged;
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange > 0 && sender is ScrollViewer sv)
                sv.ScrollToEnd();
        }
    }
}