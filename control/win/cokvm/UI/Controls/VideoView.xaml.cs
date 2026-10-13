using Windows.Devices.Input;
using Windows.Media.Core;
using Windows.System;
using Windows.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CoKVM.UI.Controls
{
    /// <summary>
    /// Displays the live video feed (GPU-accelerated via CaptureElement) and captures
    /// mouse events for forwarding. Ctrl+RightClick opens the combo key panel.
    /// Reference: control/macos/CoKVM/VideoView.swift
    /// </summary>
    public sealed partial class VideoView : UserControl
    {
        // (x, y, width, height, buttons) handlers
        public event Action<double, double, double, double, byte>? MouseMoveHandler;
        public event Action<double, double, double, double, byte>? MouseDownHandler;
        public event Action<double, double, double, double, byte>? MouseUpHandler;
        public event Action<string>? ComboKeyHandler;

        public VideoView()
        {
            InitializeComponent();

            ComboPanel.ComboKeyInvoked += combo => ComboKeyHandler?.Invoke(combo);
            ComboPopup.Closed += (s, e) => ComboPanel.RaiseDismissed();

            // Make the view focusable so it receives keyboard events; focus on click.
            IsTabStop = true;
            AllowFocusOnInteraction = true;

            // Pointer events cover move, press, release, and wheel uniformly.
            PointerMoved += OnPointerMoved;
            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            PointerWheelChanged += OnPointerWheelChanged;
        }

        /// <summary>Bind a GPU-accelerated MediaSource to the MediaPlayerElement.</summary>
        public void SetMediaSource(MediaSource? source)
        {
            PreviewPlayer.Source = source;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (ComboPopup.IsOpen) return;
            var (x, y, w, h) = GetCoords(e);
            MouseMoveHandler?.Invoke(x, y, w, h, GetButtons(e));
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Ensure the view keeps keyboard focus while interacting.
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                Focus(FocusState.Pointer);
            }

            var props = e.GetCurrentPoint(this).Properties;

            // Ctrl + RightClick opens the combo key panel.
            if (props.IsRightButtonPressed &&
                e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
            {
                ToggleComboPanel(e);
                e.Handled = true;
                return;
            }

            if (ComboPopup.IsOpen)
            {
                return;
            }

            var (x, y, w, h) = GetCoords(e);
            MouseDownHandler?.Invoke(x, y, w, h, GetButtons(e));
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (ComboPopup.IsOpen) return;
            var (x, y, w, h) = GetCoords(e);
            MouseUpHandler?.Invoke(x, y, w, h, GetButtons(e));
        }

        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // Wheel is handled at the MainWindow level via a dedicated handler;
            // but expose it here too for completeness.
            var point = e.GetCurrentPoint(this);
            double delta = point.Properties.MouseWheelDelta;
            // Normalize: Windows reports multiples of 120 per notch.
            MouseWheelHandler?.Invoke(delta / 120.0);
        }

        public event Action<double>? MouseWheelHandler;

        private (double x, double y, double w, double h) GetCoords(PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            return (point.Position.X, point.Position.Y, ActualWidth, ActualHeight);
        }

        /// <summary>
        /// Build the mouse button bitmask from currently-pressed buttons.
        /// 0x01=left, 0x02=right, 0x04=middle.
        /// </summary>
        private byte GetButtons(PointerRoutedEventArgs e)
        {
            byte buttons = 0;
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed) buttons |= 0x01;
            if (props.IsRightButtonPressed) buttons |= 0x02;
            if (props.IsMiddleButtonPressed) buttons |= 0x04;
            return buttons;
        }

        private void ToggleComboPanel(PointerRoutedEventArgs e)
        {
            if (ComboPopup.IsOpen)
            {
                ComboPopup.IsOpen = false;
                return;
            }

            var point = e.GetCurrentPoint(this);
            ComboPopup.HorizontalOffset = point.Position.X;
            ComboPopup.VerticalOffset = point.Position.Y;
            ComboPopup.IsOpen = true;
        }
    }
}