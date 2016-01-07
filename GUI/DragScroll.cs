//! \file       DragScroll.cs
//! \date       Sun Jul 06 10:47:20 2014
//! \brief      Scroll control contents by dragging.
//
// http://matthamilton.net/touchscrolling-for-scrollviewer

using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;

namespace GARbro.GUI
{
    public class TouchScrolling : DependencyObject
    {
        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public bool IsEnabled
        {
            get { return (bool)GetValue(IsEnabledProperty); }
            set { SetValue(IsEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(TouchScrolling), new UIPropertyMetadata(false, IsEnabledChanged));

        static Dictionary<object, MouseCapture> _captures = new Dictionary<object, MouseCapture>();

        static void IsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var target = d as FrameworkElement;
            if (target == null) return;

            if ((bool)e.NewValue)
            {
                target.Loaded += target_Loaded;
            }
            else
            {
                target_Unloaded(target, new RoutedEventArgs());
            }
        }

        static void target_Unloaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Target Unloaded");

            var target = sender as FrameworkElement;
            if (null == target)
                return;

            _captures.Remove(sender);

            target.Loaded -= target_Loaded;
            target.Unloaded -= target_Unloaded;
            target.PreviewMouseLeftButtonDown -= target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove -= target_PreviewMouseMove;

            target.PreviewMouseLeftButtonUp -= target_PreviewMouseLeftButtonUp;
        }

        static void target_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var target = sender as FrameworkElement;
            if (null == target)
                return;
            var scroller = FindVisualParent<ScrollViewer> (target);
            if (null == scroller)
            {
                Trace.WriteLine ("Control should be placed inside ScrollViewer for drag scrolling to work");
                return;
            }

            _captures[sender] = new MouseCapture
            {
                HorizontalOffset = scroller.HorizontalOffset,
                VerticalOffset = scroller.VerticalOffset,
                Point = e.GetPosition(scroller),
            };
        }

        static void target_Loaded(object sender, RoutedEventArgs e)
        {
            var target = sender as FrameworkElement;
            if (target == null) return;

//            Debug.WriteLine("DragScroll target Loaded", sender);

            target.Unloaded += target_Unloaded;
            target.PreviewMouseLeftButtonDown += target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove += target_PreviewMouseMove;

            target.PreviewMouseLeftButtonUp += target_PreviewMouseLeftButtonUp;
        }

        static void target_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var target = sender as FrameworkElement;
            if (target == null) return;

            target.ReleaseMouseCapture();
            target.Cursor = Cursors.Arrow;
        }

        static void target_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_captures.ContainsKey(sender)) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _captures.Remove(sender);
                return;
            }

            var target = sender as FrameworkElement;
            if (null == target)
                return;
            var scroller = FindVisualParent<ScrollViewer> (target);
            if (null == scroller)
                return;

            var capture = _captures[sender];

            var point = e.GetPosition (scroller);

            var dx = point.X - capture.Point.X;
            var dy = point.Y - capture.Point.Y;
            if (System.Math.Abs(dy) > 5 || System.Math.Abs(dx) > 5)
            {
                target.CaptureMouse();
                target.Cursor = Cursors.SizeAll;
            }
            scroller.ScrollToHorizontalOffset(capture.HorizontalOffset - dx);
            scroller.ScrollToVerticalOffset(capture.VerticalOffset - dy);
        }

        static parentItem FindVisualParent<parentItem> (DependencyObject obj) where parentItem : DependencyObject
        {
            if (null == obj)
                return null;
            DependencyObject parent = VisualTreeHelper.GetParent (obj);
            while (parent != null && !(parent is parentItem))
            {
                parent = VisualTreeHelper.GetParent (parent);
            }
            return parent as parentItem;
        }

        internal class MouseCapture
        {
            public double HorizontalOffset { get; set; }
            public double VerticalOffset { get; set; }
            public Point Point { get; set; }
        }
    }
}
