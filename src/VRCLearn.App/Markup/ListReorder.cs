using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Markup;

public static class ListReorder
{
    private const double ScrollEdge = 56;

    private const double ScrollStep = 10;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ListReorder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HandleProperty =
        DependencyProperty.RegisterAttached(
            "Handle",
            typeof(bool),
            typeof(ListReorder),
            new PropertyMetadata(false));

    private static readonly DependencyProperty BehaviorProperty =
        DependencyProperty.RegisterAttached("Behavior", typeof(ReorderBehavior), typeof(ListReorder));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetHandle(DependencyObject element, bool value) =>
        element.SetValue(HandleProperty, value);

    public static bool GetHandle(DependencyObject element) =>
        (bool)element.GetValue(HandleProperty);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl owner)
        {
            return;
        }

        if (owner.GetValue(BehaviorProperty) is ReorderBehavior existing)
        {
            existing.Detach();
            owner.ClearValue(BehaviorProperty);
        }

        if (e.NewValue is not true)
        {
            return;
        }

        var behavior = new ReorderBehavior(owner);

        owner.SetValue(BehaviorProperty, behavior);
        behavior.Attach();
    }

    private sealed class Row
    {
        public required FrameworkElement Container { get; init; }

        public required TranslateTransform Transform { get; init; }

        public Transform? Original { get; init; }

        public double Center { get; init; }
    }

    private sealed class GhostAdorner : Adorner
    {
        private readonly Image _image;
        private Point _position;

        public GhostAdorner(UIElement adorned, ImageSource source, double width, double height)
            : base(adorned)
        {
            IsHitTestVisible = false;

            _image = new Image
            {
                Source = source,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Opacity = 0.96,
            };

            AddVisualChild(_image);
        }

        protected override int VisualChildrenCount => 1;

        public void Move(double x, double y)
        {
            _position = new Point(x, y);
            InvalidateArrange();
        }

        protected override Visual GetVisualChild(int index) => _image;

        protected override Size MeasureOverride(Size constraint)
        {
            _image.Measure(constraint);

            var size = AdornedElement.RenderSize;

            return size.Width > 0 && size.Height > 0 ? size : _image.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _image.Arrange(new Rect(_position, new Size(_image.Width, _image.Height)));
            return finalSize;
        }
    }

    private sealed class ReorderBehavior
    {
        private readonly ItemsControl _owner;

        private FrameworkElement? _container;
        private object? _item;
        private INotifyCollectionChanged? _collection;
        private int _index = -1;
        private int _target = -1;
        private int _insertBefore = -1;
        private Point _start;
        private Point _grab;
        private bool _pending;
        private bool _dragging;
        private bool _ending;
        private double _rowHeight;
        private Row?[] _rows = [];
        private double[] _offsets = [];
        private ScrollViewer? _scroller;
        private AdornerLayer? _layer;
        private GhostAdorner? _ghost;
        private DispatcherTimer? _timer;
        private Window? _window;
        private Cursor? _cursor;

        public ReorderBehavior(ItemsControl owner) => _owner = owner;

        public void Attach()
        {
            _owner.PreviewMouseLeftButtonDown += OnMouseDown;
            _owner.PreviewMouseMove += OnMouseMove;
            _owner.PreviewMouseLeftButtonUp += OnMouseUp;
            _owner.LostMouseCapture += OnLostMouseCapture;
            _owner.IsVisibleChanged += OnIsVisibleChanged;
            _owner.Unloaded += OnUnloaded;
        }

        public void Detach()
        {
            _owner.PreviewMouseLeftButtonDown -= OnMouseDown;
            _owner.PreviewMouseMove -= OnMouseMove;
            _owner.PreviewMouseLeftButtonUp -= OnMouseUp;
            _owner.LostMouseCapture -= OnLostMouseCapture;
            _owner.IsVisibleChanged -= OnIsVisibleChanged;
            _owner.Unloaded -= OnUnloaded;

            Cancel();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _pending || _dragging)
            {
                return;
            }

            if (e.OriginalSource is not DependencyObject source || !IsHandle(source))
            {
                return;
            }

            if (ItemsControl.ContainerFromElement(_owner, source) is not FrameworkElement container)
            {
                return;
            }

            var item = _owner.ItemContainerGenerator.ItemFromContainer(container);
            var index = item is null ? -1 : _owner.Items.IndexOf(item);

            if (index < 0)
            {
                return;
            }

            _container = container;
            _item = item;
            _index = index;
            _start = e.GetPosition(_owner);
            _grab = e.GetPosition(container);

            if (!_owner.CaptureMouse())
            {
                ResetPending();
                return;
            }

            _pending = true;
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pending && !_dragging)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                Cancel();
                return;
            }

            var pointer = e.GetPosition(_owner);

            if (_pending)
            {
                var offset = pointer - _start;

                if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                if (!Begin(pointer))
                {
                    Cancel();
                    return;
                }
            }

            Update(pointer);
            e.Handled = true;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_pending && !_dragging)
            {
                return;
            }

            if (_dragging)
            {
                Update(e.GetPosition(_owner));
                Finish(commit: true);
            }
            else
            {
                ResetPending();

                if (_owner.IsMouseCaptured)
                {
                    _owner.ReleaseMouseCapture();
                }
            }

            e.Handled = true;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_ending)
            {
                return;
            }

            if (_dragging)
            {
                Finish(commit: false);
                return;
            }

            ResetPending();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is false)
            {
                Cancel();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Cancel();

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Cancel();

        private void OnWindowDeactivated(object? sender, EventArgs e) => Finish(commit: false);

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            AutoScroll();
            Update(Mouse.GetPosition(_owner));
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            Finish(commit: false);
        }

        private bool Begin(Point pointer)
        {
            if (_container is not { } container || _index < 0)
            {
                return false;
            }

            if (VisualTree.FindAncestor<ScrollViewer>(_owner) is not { } scroller)
            {
                return false;
            }

            var width = container.ActualWidth;
            var height = container.ActualHeight;

            if (width < 1 || height < 1)
            {
                return false;
            }

            var layer = AdornerLayer.GetAdornerLayer(scroller);

            if (layer is null)
            {
                return false;
            }

            var ghost = CreateGhost(scroller, container, width, height);

            if (ghost is null)
            {
                return false;
            }

            _scroller = scroller;
            _layer = layer;
            _rowHeight = height;
            _rows = BuildRows();
            _cursor = Mouse.OverrideCursor;
            _window = Window.GetWindow(_owner);
            _target = _index;
            _insertBefore = _index;

            layer.Add(ghost);
            _ghost = ghost;
            UpdateGhost();

            container.Visibility = Visibility.Hidden;
            Mouse.OverrideCursor = Cursors.SizeAll;

            if (_window is not null)
            {
                _window.PreviewKeyDown += OnWindowKeyDown;
                _window.Deactivated += OnWindowDeactivated;
            }

            if (_owner.Items is INotifyCollectionChanged collection)
            {
                _collection = collection;
                collection.CollectionChanged += OnCollectionChanged;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };

            _timer.Tick += OnTick;
            _timer.Start();

            _pending = false;
            _dragging = true;

            Update(pointer);

            return true;
        }

        private void Update(Point pointer)
        {
            if (!_dragging)
            {
                return;
            }

            UpdateGhost();

            var target = CountAbove(pointer.Y);

            if (target == _target)
            {
                return;
            }

            _target = target;
            _insertBefore = target <= _index ? target : target + 1;

            ApplyShifts();
        }

        private int CountAbove(double y)
        {
            var count = 0;

            for (var i = 0; i < _rows.Length; i++)
            {
                if (i != _index && _rows[i] is { } row && y > row.Center)
                {
                    count++;
                }
            }

            return Math.Clamp(count, 0, Math.Max(0, _rows.Length - 1));
        }

        private void ApplyShifts()
        {
            for (var i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] is not { } row)
                {
                    continue;
                }

                var offset = 0d;

                if (_target > _index && i > _index && i <= _target)
                {
                    offset = -_rowHeight;
                }
                else if (_target < _index && i >= _target && i < _index)
                {
                    offset = _rowHeight;
                }

                if (Math.Abs(_offsets[i] - offset) < 0.01)
                {
                    continue;
                }

                _offsets[i] = offset;
                Animate(row.Transform, offset);
            }
        }

        private static void Animate(TranslateTransform transform, double offset)
        {
            if (AnimationService.Current?.Enabled == false)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = offset;
                return;
            }

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(transform.Y, offset, AnimationService.Quick)
                {
                    EasingFunction = AnimationService.Ease,
                });
        }

        private void AutoScroll()
        {
            if (_scroller is not { } scroller || scroller.ScrollableHeight <= 0)
            {
                return;
            }

            var pointer = Mouse.GetPosition(scroller);
            var bottom = scroller.ViewportHeight - ScrollEdge;
            var intensity = 0d;

            if (pointer.Y < ScrollEdge)
            {
                intensity = -Math.Clamp(1 - Math.Max(0, pointer.Y) / ScrollEdge, 0, 1);
            }
            else if (pointer.Y > bottom)
            {
                intensity = Math.Clamp((pointer.Y - bottom) / ScrollEdge, 0, 1);
            }

            if (intensity == 0)
            {
                return;
            }

            var offset = Math.Clamp(
                scroller.VerticalOffset + intensity * ScrollStep,
                0,
                scroller.ScrollableHeight);

            if (Math.Abs(offset - scroller.VerticalOffset) < 0.5)
            {
                return;
            }

            scroller.ScrollToVerticalOffset(offset);
        }

        private void UpdateGhost()
        {
            if (_ghost is not { } ghost || _scroller is not { } scroller)
            {
                return;
            }

            var pointer = Mouse.GetPosition(scroller);

            ghost.Move(pointer.X - _grab.X, pointer.Y - _grab.Y);
        }

        private Row?[] BuildRows()
        {
            var count = _owner.Items.Count;
            var rows = new Row?[count];

            _offsets = new double[count];

            for (var i = 0; i < count; i++)
            {
                if (_owner.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                {
                    continue;
                }

                var transform = new TranslateTransform();
                var original = container.RenderTransform;

                container.RenderTransform = transform;

                var top = container.TransformToAncestor(_owner).Transform(new Point()).Y;

                rows[i] = new Row
                {
                    Container = container,
                    Transform = transform,
                    Original = original,
                    Center = top + container.ActualHeight / 2,
                };
            }

            return rows;
        }

        private static GhostAdorner? CreateGhost(
            ScrollViewer scroller,
            FrameworkElement container,
            double width,
            double height)
        {
            var dpi = VisualTreeHelper.GetDpi(container);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);

            var visual = new DrawingVisual();
            var bounds = new Rect(0, 0, width, height);
            var brush = new VisualBrush(container) { Stretch = Stretch.Fill };

            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(brush, null, bounds);
            }

            bitmap.Render(visual);
            bitmap.Freeze();

            return new GhostAdorner(scroller, bitmap, width, height);
        }

        private void Finish(bool commit)
        {
            if (_ending)
            {
                return;
            }

            _ending = true;

            var item = _item;
            var insertBefore = _insertBefore;
            var moved = _target != _index;
            var owner = _owner;

            StopDrag();
            ResetState();

            _ending = false;

            if (commit && moved && item is not null && owner.DataContext is IReorderableList list)
            {
                list.Move(item, insertBefore);
            }
        }

        private void StopDrag()
        {
            if (_timer is { } timer)
            {
                timer.Stop();
                timer.Tick -= OnTick;
                _timer = null;
            }

            if (_window is { } window)
            {
                window.PreviewKeyDown -= OnWindowKeyDown;
                window.Deactivated -= OnWindowDeactivated;
                _window = null;
            }

            if (_collection is { } collection)
            {
                collection.CollectionChanged -= OnCollectionChanged;
                _collection = null;
            }

            if (_ghost is { } ghost)
            {
                _layer?.Remove(ghost);
                _ghost = null;
                _layer = null;
            }

            foreach (var row in _rows)
            {
                if (row is null)
                {
                    continue;
                }

                row.Transform.BeginAnimation(TranslateTransform.YProperty, null);
                row.Transform.Y = 0;
                row.Container.RenderTransform = row.Original;
            }

            if (_container is { } container)
            {
                container.Visibility = Visibility.Visible;
            }

            if (_owner.IsMouseCaptured)
            {
                _owner.ReleaseMouseCapture();
            }

            Mouse.OverrideCursor = _cursor;
            _cursor = null;

            _pending = false;
            _dragging = false;
        }

        private void ResetState()
        {
            _container = null;
            _item = null;
            _index = -1;
            _target = -1;
            _insertBefore = -1;
            _rowHeight = 0;
            _rows = [];
            _offsets = [];
            _scroller = null;
        }

        private void ResetPending()
        {
            _pending = false;
            _container = null;
            _item = null;
            _index = -1;
        }

        private void Cancel()
        {
            if (_dragging)
            {
                Finish(commit: false);
                return;
            }

            ResetPending();

            if (_owner.IsMouseCaptured)
            {
                _owner.ReleaseMouseCapture();
            }
        }

        private bool IsHandle(DependencyObject source)
        {
            for (var node = source; node is not null; node = ParentOf(node))
            {
                if (ReferenceEquals(node, _owner))
                {
                    return false;
                }

                if (node is FrameworkElement element && GetHandle(element))
                {
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject? ParentOf(DependencyObject node) =>
            node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    }
}
