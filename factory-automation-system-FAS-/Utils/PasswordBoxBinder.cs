// Utils/PasswordBoxBinder.cs
using System.Windows;
using System.Windows.Controls;

namespace factory_automation_system_FAS_.Utils
{
    /// <summary>
    /// PasswordBox를 MVVM 바인딩 가능하게 하는 간단한 AttachedProperty
    /// </summary>
    public static class PasswordBoxBinder
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxBinder),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

        public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxBinder),
                new PropertyMetadata(false, OnBindPasswordChanged));

        public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);
        public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

        private static readonly DependencyProperty UpdatingPasswordProperty =
            DependencyProperty.RegisterAttached("UpdatingPassword", typeof(bool), typeof(PasswordBoxBinder), new PropertyMetadata(false));

        private static bool GetUpdatingPassword(DependencyObject obj) => (bool)obj.GetValue(UpdatingPasswordProperty);
        private static void SetUpdatingPassword(DependencyObject obj, bool value) => obj.SetValue(UpdatingPasswordProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb) return;

            pb.PasswordChanged -= HandlePasswordChanged;

            if (!GetBindPassword(pb)) return;

            if (!GetUpdatingPassword(pb))
                pb.Password = e.NewValue as string ?? "";

            pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb) return;

            var bind = (bool)e.NewValue;

            pb.PasswordChanged -= HandlePasswordChanged;

            if (bind)
                pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            SetUpdatingPassword(pb, true);
            SetBoundPassword(pb, pb.Password);
            SetUpdatingPassword(pb, false);
        }
    }
}
