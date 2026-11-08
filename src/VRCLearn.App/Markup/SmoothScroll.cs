using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRCLearn.App.Services;

namespace VRCLearn.App.Markup;

public static class SmoothScroll
{
    private const double PixelsPerLine = 40;

    /// <summary>趋近的时间常数（秒）。</summary>
    private const double TimeConstant = 0.09;

    private const double Settle = 0.5;
    private static readonly TimeSpan JumpDuration = TimeSpan.FromMilliseconds(480);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private sealed class Glide
    {
        public double Target { get; set; }

        public double Position { get; set; }
        public Func<double?>? JumpTarget { get; init; }
        public long JumpStarted { get; init; }
    }

    private static readonly Dictionary<ScrollViewer, Glide> Glides = [];
    private static bool _running;
    private static long _lastFrame;

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void ScrollTo(ScrollViewer viewer, Func<double?> target)
    {
        if (AnimationService.Current?.Enabled == false)
        {
            Glides.Remove(viewer);
            if (target() is { } offset)
            {
                viewer.ScrollToVerticalOffset(offset);
            }

            return;
        }

        Glides[viewer] = new Glide
        {
            Position = viewer.VerticalOffset,
            JumpTarget = target,
            JumpStarted = Stopwatch.GetTimestamp(),
        };
        Start();
    }

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is ScrollViewer viewer)
        {
            viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            viewer.PreviewMouseDown -= OnScrollInput;
            viewer.PreviewKeyDown -= OnScrollInput;
            viewer.ScrollChanged -= OnScrollChanged;
            viewer.Unloaded -= OnUnloaded;
            Glides.Remove(viewer);

            if ((bool)e.NewValue)
            {
                viewer.PreviewMouseWheel += OnPreviewMouseWheel;
                viewer.PreviewMouseDown += OnScrollInput;
                viewer.PreviewKeyDown += OnScrollInput;
                viewer.ScrollChanged += OnScrollChanged;
                viewer.Unloaded += OnUnloaded;
            }

            return;
        }

        if (element is not FrameworkElement host)
        {
            return;
        }

        host.Loaded -= OnHostLoaded;

        if ((bool)e.NewValue)
        {
            host.Loaded += OnHostLoaded;

            if (host.IsLoaded)
            {
                OnHostLoaded(host, new RoutedEventArgs());
            }
        }
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject host && VisualTree.FindDescendant<ScrollViewer>(host) is { } viewer)
        {
            SetIsEnabled(viewer, true);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Glides.Remove(viewer);
        }
    }

    private static void OnScrollInput(object sender, InputEventArgs e)
    {
        if (sender is ScrollViewer viewer && Glides.TryGetValue(viewer, out var glide) && glide.JumpTarget is not null)
        {
            Glides.Remove(viewer);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer
            || e.VerticalChange == 0
            || !Glides.TryGetValue(viewer, out var glide)
            || glide.JumpTarget is not null
            || Math.Abs(glide.Position - viewer.VerticalOffset) < Settle)
        {
            return;
        }

        glide.Position = viewer.VerticalOffset;
        glide.Target = viewer.VerticalOffset;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var lines = SystemParameters.WheelScrollLines;

        if (lines == 0)
        {
            e.Handled = true;
            return;
        }

        var step = lines < 0 ? viewer.ViewportHeight : lines * PixelsPerLine;

        if (!Glides.TryGetValue(viewer, out var glide) || glide.JumpTarget is not null)
        {
            Glides[viewer] = glide = new Glide()
            {
                Position = viewer.VerticalOffset,
                Target = viewer.VerticalOffset,
            };
        }

        glide.Target = Math.Clamp(glide.Target - (e.Delta / 120.0 * step), 0, viewer.ScrollableHeight);

        e.Handled = true;

        if (AnimationService.Current?.Enabled == false)
        {
            glide.Position = glide.Target;
            viewer.ScrollToVerticalOffset(glide.Target);
            Glides.Remove(viewer);

            return;
        }

        Start();
    }

    private static void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastFrame = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var delta = _lastFrame == 0 ? 1.0 / 60 : (now - _lastFrame) / (double)Stopwatch.Frequency;
        _lastFrame = now;

        var factor = 1 - Math.Exp(-Math.Clamp(delta, 0, 0.05) / TimeConstant);
        var moving = false;
        List<ScrollViewer>? settled = null;

        foreach (var (viewer, glide) in Glides)
        {
            if (glide.JumpTarget is { } target)
            {
                if (target() is not { } offset)
                {
                    (settled ??= []).Add(viewer);
                    continue;
                }

                // Rebase after virtualized layout changes, while keeping fractional movement between frames.
                if (Math.Abs(glide.Position - viewer.VerticalOffset) > 1)
                {
                    glide.Position = viewer.VerticalOffset;
                }

                glide.Target = Math.Clamp(offset, 0, viewer.ScrollableHeight);
            }

            var remaining = glide.Target - glide.Position;

            if (Math.Abs(remaining) < Settle
                || (glide.JumpTarget is not null && Stopwatch.GetElapsedTime(glide.JumpStarted, now) >= JumpDuration))
            {
                if (glide.Position != glide.Target)
                {
                    glide.Position = glide.Target;
                    viewer.ScrollToVerticalOffset(glide.Target);
                }

                (settled ??= []).Add(viewer);
                continue;
            }

            glide.Position += remaining * factor;
            viewer.ScrollToVerticalOffset(glide.Position);
            moving = true;
        }

        if (settled is not null)
        {
            foreach (var viewer in settled)
            {
                Glides.Remove(viewer);
            }
        }

        if (moving)
        {
            return;
        }

        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }
}
