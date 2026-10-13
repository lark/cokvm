using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using CoKVM.Input;
using CoKVM.Protocol;
using CoKVM.Services;
using CoKVM.UI.Models;
using WinRT.Interop;

namespace CoKVM
{
    /// <summary>
    /// Main application window. Owns all services and input managers, and wires
    /// keyboard / mouse / LED events to the Bluetooth service.
    /// Reference: control/macos/CoKVM/AppDelegate.swift
    /// </summary>
    public sealed partial class MainWindow : Window,
        IBluetoothDelegate, IVideoCaptureDelegate
    {
        private VideoCaptureService? _videoCapture;
        private BluetoothService? _bluetooth;
        private KeyboardManager? _keyboard;
        private MouseManager? _mouse;

        private bool _videoReady;
        private bool _btReady;
        private bool _btDiscovered;
        private bool _lastCapsLock;
        private int _prevDeviceCount;
        private bool _starting; // true while a video Start() is in progress (guards double-start)

        private const double StatusBarHeight = 30.0;
        private const double Aspect = 16.0 / 9.0;

        public MainWindow()
        {
            InitializeComponent();

            SetupWindow();
            SetupServices();
            SetupEventHandling();
        }

        private double _dpiScale = 1.0; // monitor DPI scale (e.g. 1.5 at 150%)

        // Window chrome (title bar + borders) and status bar, in PHYSICAL pixels at
        // the monitor DPI. Computed once in SetupWindow; constant for fixed style/DPI.
        // Keeping these lets every resize derive the exact window height from the video
        // width without re-running AdjustWindowRectExForDpi, and without the off-by-
        // chrome error that produced black side bars.
        private int _hChrome;
        private int _vChrome;
        private int _statusBarPhysical;

        private void SetupWindow()
        {
            // Size the window so the VIDEO VIEW is exactly a standard 16:9 physical
            // resolution (pixel-perfect 1:1 with the captured video). We compute the
            // required client size (video + 30-DIP status bar) and add the window
            // chrome (title bar + borders) via AdjustWindowRectExForDpi.
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            uint dpi = Win32.GetDpiForWindow(hwnd);
            _dpiScale = dpi / 96.0;

            int style = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);

            _statusBarPhysical = (int)Math.Round(StatusBarHeight * _dpiScale);

            // Probe the chrome with a throwaway rect: AdjustWindowRectExForDpi expands
            // {0,0,1000,1000+statusBar} into the full window rect; the excess over the
            // input is the chrome. It is independent of the input size, so this is valid
            // for every subsequent resize.
            var probe = new Win32.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 1000 + _statusBarPhysical };
            Win32.AdjustWindowRectExForDpi(ref probe, (uint)style, false, (uint)exStyle, dpi);
            _hChrome = (probe.Right - probe.Left) - 1000;
            _vChrome = (probe.Bottom - probe.Top) - (1000 + _statusBarPhysical);

            var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Nearest);
            int workW = displayArea.WorkArea.Width;
            int workH = displayArea.WorkArea.Height;

            // Candidate (videoW, videoH) PHYSICAL sizes, largest first.
            int[][] sizes = { new[] { 1920, 1080 }, new[] { 1440, 810 }, new[] { 1280, 720 } };
            int videoW = 1280, videoH = 720; // fallback
            foreach (var s in sizes)
            {
                var win = WindowSizeForVideo(s[0], s[1]);
                if (win.w <= workW && win.h <= workH)
                {
                    videoW = s[0]; videoH = s[1];
                    break;
                }
            }

            var size = WindowSizeForVideo(videoW, videoH);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(size.w, size.h));
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);

            // Resizing: height is always derived from width (see OnWindowSizeChanged),
            // so only width effectively drives the layout — height-only and corner
            // drags snap back to the 16:9 + status-bar height.
            AppWindow.Closing += OnClosing;
            this.SizeChanged += OnWindowSizeChanged;
        }

        // Full window physical size for a video of videoW x videoH physical pixels plus
        // the status bar, using the precomputed chrome.
        private (int w, int h) WindowSizeForVideo(int videoW, int videoH)
        {
            return (videoW + _hChrome, videoH + _statusBarPhysical + _vChrome);
        }

        private bool _resizing;

        private void OnWindowSizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
        {
            if (_resizing) { UpdateStatusBar(); return; }

            // AppWindow.Size / AppWindow.Resize use PHYSICAL pixels. Derive the window
            // height from the width so the video box is exactly 16:9 (video width =
            // window width - horizontal chrome; video height = video width * 9/16).
            // Accounting for chrome here (previously missing) is what removes the black
            // side bars and prevents the cumulative drift.
            var size = AppWindow.Size;
            int width = size.Width;

            int videoWidth = width - _hChrome;
            if (videoWidth < 320) videoWidth = 320; // don't collapse to nothing
            int videoHeight = (int)Math.Round(videoWidth * 9.0 / 16.0);
            int windowHeight = videoHeight + _statusBarPhysical + _vChrome;

            if (Math.Abs(size.Height - windowHeight) > 1)
            {
                _resizing = true;
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, windowHeight));
                _resizing = false;
            }
            UpdateStatusBar();
        }

        private void SetupServices()
        {
            _keyboard = new KeyboardManager();
            _mouse = new MouseManager();

            _videoCapture = new VideoCaptureService(this);

            _bluetooth = new BluetoothService(this);
            _bluetooth.VideoReadyProvider = () => StatusBar.IsVideoActive();
            _bluetooth.Start();

            // Video capture starts reactively: the VideoCaptureService's DeviceWatcher
            // fires OnDevicesChanged for already-present devices (launch) and for
            // hot-plugged devices, and OnDevicesChanged auto-starts capture. No
            // explicit Start() here — that would race with the watcher's initial
            // enumeration and double-start.

            StatusBar.VideoButtonClicked += HandleVideoButtonClick;
            StatusBar.BluetoothButtonClicked += () => _bluetooth?.InitiatePairing();
        }

        private void SetupEventHandling()
        {
            // Capture keyboard at the WINDOW ROOT via the tunneling PreviewKeyDown /
            // PreviewKeyUp events — NOT on the VideoView. Keyboard focus lands on the
            // MediaPlayerElement child, which swallows the bubbling KeyDown/KeyUp
            // events so they never reach the view's handlers. Tunneling preview
            // events fire at the root first regardless of which descendant holds
            // focus, so every keypress is captured (and the indicator updates).
            var root = (FrameworkElement)this.Content;
            root.PreviewKeyDown += OnKeyDown;
            root.PreviewKeyUp += OnKeyUp;

            // Mouse events from the video view.
            VideoView.MouseMoveHandler += OnMouseMove;
            VideoView.MouseDownHandler += OnMouseMove;
            VideoView.MouseUpHandler += OnMouseMove;
            VideoView.MouseWheelHandler += OnMouseWheel;
            VideoView.ComboKeyHandler += SendComboKey;

            // Keep focus within the window on pointer interaction.
            VideoView.Loaded += (s, e) => VideoView.Focus(FocusState.Programmatic);

            // LED sync strategy: no polling. Force one sync whenever the window (or
            // the video view) (re)gains focus — catches toggles made while unfocused.
            // While focused, Caps Lock key-down drives incremental syncs
            // (see OnKeyDown); key-up is ignored (Windows toggles on key-down).
            VideoView.GotFocus += (s, e) => SyncLedState();
            this.Activated += OnWindowActivated;
        }

        private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            // On every (re)activation, force a fresh LED sync. Covers the case where
            // Caps Lock was toggled elsewhere while this window had no focus.
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                SyncLedState();
            }
        }

        // ---- Keyboard handling ----

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Caps Lock toggles on key-down (Windows semantics); key-up is ignored.
            // Defer the sync one dispatcher tick so the OS has applied the toggle
            // before we read the lock state.
            if (e.Key == VirtualKey.CapitalLock)
            {
                // Leave Handled=false so the OS processes the toggle normally.
                DispatcherQueue.TryEnqueue(SyncLedState);
                return;
            }

            // Let Alt+F4 close the window (system command).
            if (e.Key == VirtualKey.F4 && IsDown(VirtualKey.Menu)) return;

            UpdateModifiersFromCurrentState();
            var payload = _keyboard!.OnKeyDown(e.Key);
            Log($"KeyDown {e.Key} -> {(payload != null ? "sent" : "null")}");
            if (payload != null) _bluetooth?.SendPayload(payload);
            e.Handled = true; // prevent child default actions (media keys) + focus traversal
            UpdateStatusBar();
        }

        private void OnKeyUp(object sender, KeyRoutedEventArgs e)
        {
            // Caps Lock: toggle is on key-down; ignore key-up entirely to
            // avoid redundant syncs.
            if (e.Key == VirtualKey.CapitalLock)
            {
                return;
            }

            if (e.Key == VirtualKey.F4 && IsDown(VirtualKey.Menu)) return;

            UpdateModifiersFromCurrentState();
            var payload = _keyboard!.OnKeyUp(e.Key);
            Log($"KeyUp {e.Key} -> {(payload != null ? "sent" : "null")}");
            if (payload != null) _bluetooth?.SendPayload(payload);
            e.Handled = true;
            UpdateStatusBar();
        }

        private void UpdateModifiersFromCurrentState()
        {
            var pressed = new List<VirtualKey>();
            if (IsDown(VirtualKey.LeftControl)) pressed.Add(VirtualKey.LeftControl);
            if (IsDown(VirtualKey.RightControl)) pressed.Add(VirtualKey.RightControl);
            if (IsDown(VirtualKey.LeftShift)) pressed.Add(VirtualKey.LeftShift);
            if (IsDown(VirtualKey.RightShift)) pressed.Add(VirtualKey.RightShift);
            if (IsDown(VirtualKey.LeftMenu)) pressed.Add(VirtualKey.LeftMenu);
            if (IsDown(VirtualKey.RightMenu)) pressed.Add(VirtualKey.RightMenu);
            if (IsDown(VirtualKey.LeftWindows)) pressed.Add(VirtualKey.LeftWindows);
            if (IsDown(VirtualKey.RightWindows)) pressed.Add(VirtualKey.RightWindows);
            _keyboard!.UpdateModifiers(pressed);
        }

        private static bool IsDown(VirtualKey key)
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
            return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        // ---- LED sync ----

        private void SyncLedState()
        {
            bool capsLock = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.CapitalLock) & CoreVirtualKeyStates.Locked) == CoreVirtualKeyStates.Locked;

            if (capsLock != _lastCapsLock)
            {
                _lastCapsLock = capsLock;
                var payload = VssPayload.LedStatus(capsLock);
                _bluetooth?.SendPayload(payload);
                UpdateStatusBar();
            }
        }

        // ---- Mouse handling ----

        private void OnMouseMove(double x, double y, double w, double h, byte buttons)
        {
            var payload = _mouse!.OnMouseMove(x, y, w, h, buttons);
            _bluetooth?.SendPayload(payload);
            UpdateStatusBar();
        }

        private void OnMouseWheel(double deltaY)
        {
            Log($"MouseWheel dy={deltaY}");
            var payload = _mouse!.OnMouseWheel(deltaY);
            _bluetooth?.SendPayload(payload);
            UpdateStatusBar();
        }

        private static void Log(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoKVM");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "input.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        // ---- Combo keys ----

        private void SendComboKey(string combo)
        {
            if (_bluetooth == null) return;
            var (modifiers, keycodes) = KeyboardManager.ParseCombo(combo);

            var down = VssPayload.Keyboard(modifiers, keycodes);
            _bluetooth.SendPayload(down);

            // Release after a short delay (matches macOS 50ms).
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (s, _) =>
            {
                timer.Stop();
                var up = VssPayload.Keyboard(0, Array.Empty<byte>());
                _bluetooth.SendPayload(up);
            };
            timer.Start();
        }

        // ---- Video controls ----

        private void HandleVideoButtonClick()
        {
            if (_videoReady)
            {
                _videoCapture?.Stop();
                var count = _videoCapture?.AvailableDevices.Count ?? 0;
                StatusBar.SetVideoStatus(count > 1 ? VideoStatus.MultiSourcePaused : VideoStatus.SingleSourcePaused);
            }
            else
            {
                var devices = _videoCapture?.AvailableDevices ?? new List<VideoCaptureDeviceInfo>();
                if (devices.Count >= 1)
                {
                    if (devices.Count > 1)
                    {
                        ShowVideoSourceMenu();
                    }
                    else
                    {
                        _videoCapture?.Start();
                        StatusBar.SetVideoStatus(VideoStatus.SingleSourceActive);
                    }
                }
            }
        }

        private void ShowVideoSourceMenu()
        {
            var devices = _videoCapture?.AvailableDevices ?? new List<VideoCaptureDeviceInfo>();
            if (devices.Count == 0) return;

            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            foreach (var device in devices)
            {
                var name = device.Name;
                var id = device.Id;
                var suffix = id.Length >= 8 ? id[^8..] : id;
                var displayName = devices.Count(d => d.Name == name) > 1
                    ? $"{name} ({suffix})"
                    : name;

                var item = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                {
                    Text = displayName,
                    Tag = device
                };
                item.Click += (s, e) =>
                {
                    if (s is Microsoft.UI.Xaml.Controls.MenuFlyoutItem mfi && mfi.Tag is VideoCaptureDeviceInfo di)
                    {
                        _videoCapture?.Stop();
                        _videoCapture?.Start(di);
                        var c = _videoCapture?.AvailableDevices.Count ?? 0;
                        StatusBar.SetVideoStatus(c > 1 ? VideoStatus.MultiSourceActive : VideoStatus.SingleSourceActive);
                    }
                };
                menu.Items.Add(item);
            }

            // Position near the video button area (bottom-right of status bar).
            menu.ShowAt(StatusBar, new Windows.Foundation.Point(StatusBar.ActualWidth - 60, 0));
        }

        // ---- IVideoCaptureDelegate ----

        public void OnVideoReady(bool ready, string format)
        {
            _videoReady = ready;
            _starting = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                // Bind (or unbind) the GPU-accelerated MediaSource to the preview.
                // Equivalent to macOS onVideoReady -> videoView.setSession(captureSession).
                VideoView.SetMediaSource(ready ? _videoCapture?.GetMediaSource() : null);

                AppWindow.Title = string.IsNullOrEmpty(format) ? "CoKVM" : $"CoKVM — {format}";
                UpdateVideoStatusFromDevices(_videoCapture?.AvailableDevices.Count ?? 0);
                UpdateStatusBar();
            });
        }

        public void OnDevicesChanged(IReadOnlyList<VideoCaptureDeviceInfo> devices)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                int count = devices.Count;
                bool wasZero = _prevDeviceCount == 0;
                _prevDeviceCount = count;
                UpdateVideoStatusFromDevices(count);

                // Auto-start capture when a device appears and we're idle — covers both
                // launch (watcher's initial enumeration) and hot-plug. Single source
                // starts directly; multi-source starts the first device (click to pause,
                // click again for the source menu). Guarded so the recursive
                // RefreshDevicesAsync inside Start() can't double-start.
                if (count > 0 && wasZero && !_videoReady && !_starting)
                {
                    _starting = true;
                    _videoCapture?.Start();
                }
            });
        }

        private void UpdateVideoStatusFromDevices(int count)
        {
            if (count == 0)
            {
                StatusBar.SetVideoStatus(VideoStatus.NoDevice);
            }
            else if (!_videoReady)
            {
                StatusBar.SetVideoStatus(count > 1 ? VideoStatus.MultiSourcePaused : VideoStatus.SingleSourcePaused);
            }
            else
            {
                StatusBar.SetVideoStatus(count > 1 ? VideoStatus.MultiSourceActive : VideoStatus.SingleSourceActive);
            }
        }

        // ---- IBluetoothDelegate ----
        // Note: these fire on background threads (DeviceWatcher); marshal to UI.

        public void OnBluetoothReady(bool ready)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _btReady = ready;
                UpdateStatusBar();
            });
        }

        public void OnBluetoothDeviceDiscovered(bool discovered)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _btDiscovered = discovered;
                UpdateStatusBar();
            });
        }

        public void OnBluetoothStateChanged(BluetoothState state)
        {
            DispatcherQueue.TryEnqueue(UpdateStatusBar);
        }

        // ---- Status bar ----

        private void UpdateStatusBar()
        {
            if (_keyboard == null || _mouse == null) return;

            // XamlRoot.RasterizationScale is the monitor DPI scale (e.g. 1.5 at 150%).
            // Report viewport as logical (DIP) @ physical, matching the macOS
            // backingScaleFactor behaviour.
            double scale = VideoView.XamlRoot?.RasterizationScale ?? 1.0;
            double logicalW = VideoView.ActualWidth;
            double logicalH = VideoView.ActualHeight;

            StatusBar.Update(
                viewportSize: (logicalW, logicalH),
                physicalSize: (logicalW * scale, logicalH * scale),
                mousePos: _mouse.LastWinPos,
                mappedPos: _mouse.LastMappedPos,
                modifiers: _keyboard.Modifiers,
                activeKeycodes: _keyboard.ActiveKeycodes,
                btReady: _btReady,
                btDiscovered: _btDiscovered,
                capsLockOn: _lastCapsLock);
        }

        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            _videoCapture?.Stop();
            _bluetooth?.Stop();
        }
    }

    internal static class Win32
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool AdjustWindowRectExForDpi(
            ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle, uint dpi);
    }
}