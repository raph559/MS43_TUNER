using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// Slides a shared selection marker beneath <c>Border.seg</c> controls while the motion layer is enabled.
/// Without that layer, segments fill immediately.
/// </summary>
public static class SegmentSlide
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("IsEnabled", typeof(SegmentSlide));

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);
    private static readonly ConditionalWeakTable<Border, Slider> Sliders = new();

    static SegmentSlide()
    {
        IsEnabledProperty.Changed.AddClassHandler<Border>((border, e) =>
        {
            if (e.NewValue is true)
            {
                Sliders.GetValue(border, b => new Slider(b)).Attach();
            }
            else if (Sliders.TryGetValue(border, out var slider))
            {
                slider.Detach();
                Sliders.Remove(border);
            }
        });
    }

    public static bool GetIsEnabled(Border border) => border.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Border border, bool value) => border.SetValue(IsEnabledProperty, value);

    private sealed class Slider(Border border)
    {
        private readonly Panel _host = new();
        private readonly Border _pill = new()
        {
            Classes = { "segpill" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };

        private Control? _content;
        private Rect? _placed;
        private DateTime _slideUntil;

        public void Attach()
        {
            if (_content is not null || border.Child is not Control content)
            {
                return;
            }

            // The segments stay where they are; the pill goes under them in a panel that takes the border's child slot.
            _content = content;
            border.Child = null;
            _host.Children.Add(_pill);
            _host.Children.Add(content);
            border.Child = _host;
            border.Classes.Add("sliding");
            _host.AddHandler(ToggleButton.IsCheckedChangedEvent, OnChecked, RoutingStrategies.Bubble);
            _host.LayoutUpdated += OnLayout;
        }

        public void Detach()
        {
            if (_content is not { } content)
            {
                return;
            }

            _host.RemoveHandler(ToggleButton.IsCheckedChangedEvent, OnChecked);
            _host.LayoutUpdated -= OnLayout;
            border.Classes.Remove("sliding");
            border.Child = null;
            _host.Children.Clear();
            border.Child = content;
            _content = null;
            _placed = null;
        }

        // A pick slides; anything else (first layout, a resize, a label that changed width) places the pill at once.
        private void OnChecked(object? sender, RoutedEventArgs e) => Place(animate: true);

        // The checked label turns SemiBold and changes width a pass later: during a slide that re-aims it, never snaps it.
        private void OnLayout(object? sender, EventArgs e) => Place(animate: DateTime.UtcNow < _slideUntil);

        private void Place(bool animate)
        {
            var target = Checked();
            if (target is null)
            {
                // Between one segment unchecking and the next checking nothing is checked; only a settled layout hides the pill.
                if (!animate)
                {
                    _pill.IsVisible = false;
                    _placed = null;
                }

                return;
            }

            if (target.TranslatePoint(default, _host) is not { } at)
            {
                return;
            }

            var rect = new Rect(at, target.Bounds.Size);
            if (_placed == rect)
            {
                return;
            }

            // Layout passes during a slide find the pill already aimed at its target, so they never cut it short.
            var slide = animate && _placed is not null && _pill.IsVisible;
            if (slide && DateTime.UtcNow >= _slideUntil)
            {
                _slideUntil = DateTime.UtcNow + Duration;
            }

            _pill.IsVisible = true;
            // Replacing running transitions would snap the pill to their end, so a re-aim keeps the ones it has.
            if (!slide)
            {
                _pill.Transitions = null;
            }
            else if (_pill.Transitions is null)
            {
                _pill.Transitions =
                [
                    new ThicknessTransition { Property = Layoutable.MarginProperty, Duration = Duration, Easing = new CubicEaseOut() },
                    new DoubleTransition { Property = Layoutable.WidthProperty, Duration = Duration, Easing = new CubicEaseOut() },
                ];
            }
            _pill.CornerRadius = target.CornerRadius;
            _pill.Height = rect.Height;
            _pill.Width = rect.Width;
            _pill.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            _placed = rect;
        }

        private ToggleButton? Checked() =>
            _content?.GetVisualDescendants().OfType<ToggleButton>()
                .FirstOrDefault(toggle => toggle.IsChecked == true && toggle.IsEffectivelyVisible && toggle.Classes.Contains("seg") && Owns(toggle));

        // Only this control's segments: a segmented control nested inside another keeps its own pill.
        private bool Owns(Visual toggle)
        {
            for (var v = toggle.GetVisualParent(); v is not null; v = v.GetVisualParent())
            {
                if (v is Border b && b.Classes.Contains("seg"))
                {
                    return ReferenceEquals(b, border);
                }
            }

            return false;
        }
    }
}
