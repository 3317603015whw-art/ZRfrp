using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfColor = System.Windows.Media.Color;

namespace FrpDesktop;

public sealed class HalfWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0)
        {
            return 0d;
        }

        var gap = 12d;
        if (parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedGap))
        {
            gap = parsedGap;
        }

        return Math.Max(240d, Math.Floor((width - gap) / 2d));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class EmptyToPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var placeholder = parameter as string ?? "N/A";
        return value is string text && !string.IsNullOrWhiteSpace(text) ? text : placeholder;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class LatencyBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            if (text.Contains("测速中", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(WpfColor.FromRgb(96, 165, 250));
            }

            if (text.Contains("待测速", StringComparison.OrdinalIgnoreCase) || text.Contains("未配置", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(WpfColor.FromRgb(145, 164, 184));
            }

            if ((text.Contains("超时", StringComparison.OrdinalIgnoreCase) || text.Contains("失败", StringComparison.OrdinalIgnoreCase))
                && !text.Contains("ms", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(WpfColor.FromRgb(251, 113, 133));
            }

            var numberText = text.Replace("ms", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var latency))
            {
                return BrushForLatency(latency);
            }
        }

        if (value is FrpProfile { IsLatencyTesting: true })
        {
            return new SolidColorBrush(WpfColor.FromRgb(96, 165, 250));
        }

        if (value is not FrpProfile profile || profile.LatencyMs is null)
        {
            return new SolidColorBrush(WpfColor.FromRgb(145, 164, 184));
        }

        return BrushForLatency(profile.LatencyMs.Value);
    }

    private static SolidColorBrush BrushForLatency(int latency)
    {
        return latency switch
        {
            <= 80 => new SolidColorBrush(WpfColor.FromRgb(91, 226, 166)),
            <= 180 => new SolidColorBrush(WpfColor.FromRgb(250, 204, 21)),
            <= 350 => new SolidColorBrush(WpfColor.FromRgb(251, 146, 60)),
            _ => new SolidColorBrush(WpfColor.FromRgb(251, 113, 133))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0d, OnTargetOffsetChanged));

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    private static void OnEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += HandlePreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= HandlePreviewMouseWheel;
        }
    }

    private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        var scrollViewer = dependencyObject as ScrollViewer ?? FindVisualChild<ScrollViewer>(dependencyObject);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;
        var currentTarget = (double)scrollViewer.GetValue(TargetOffsetProperty);
        if (Math.Abs(currentTarget) < 0.1)
        {
            currentTarget = scrollViewer.VerticalOffset;
        }

        var delta = e.Delta > 0 ? -72d : 72d;
        var nextOffset = Math.Clamp(currentTarget + delta, 0d, scrollViewer.ScrollableHeight);
        scrollViewer.SetValue(TargetOffsetProperty, nextOffset);

        var animation = new DoubleAnimation
        {
            To = nextOffset,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        scrollViewer.BeginAnimation(TargetOffsetProperty, animation, HandoffBehavior.Compose);
    }

    private static void OnTargetOffsetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
