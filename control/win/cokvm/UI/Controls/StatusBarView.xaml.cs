using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CoKVM.Services;
using CoKVM.UI.Models;

namespace CoKVM.UI.Controls
{
    /// <summary>
    /// Bottom status bar: viewport / mouse / keyboard labels, plus CapsLock,
    /// Video and Bluetooth status buttons.
    /// Reference: control/macos/CoKVM/StatusBarView.swift
    /// </summary>
    public sealed partial class StatusBarView : UserControl
    {
        // Segoe MDL2 Assets glyphs (Unicode escapes to avoid encoding loss).
        private const string GlyphVideo = "";     // Video camera
        private const string GlyphVideoChat = ""; // Multi-source
        private const string GlyphPause = "";     // Paused
        private const string GlyphQuestion = "";  // Unknown / no device
        private const string GlyphBluetooth = ""; // Bluetooth

        public event Action? VideoButtonClicked;
        public event Action? BluetoothButtonClicked;

        private VideoStatus _videoStatus = VideoStatus.NoDevice;

        public StatusBarView()
        {
            InitializeComponent();
        }

        public bool IsVideoActive() => _videoStatus.IsActive();

        // ---- Status updates ----

        public void Update(
            (double w, double h) viewportSize,
            (double w, double h) physicalSize,
            (int x, int y) mousePos,
            (short x, short y) mappedPos,
            byte modifiers,
            byte[] activeKeycodes,
            bool btReady,
            bool btDiscovered,
            bool capsLockOn)
        {
            ViewportLabel.Text = $"{(int)viewportSize.w}x{(int)viewportSize.h} @ {(int)physicalSize.w}x{(int)physicalSize.h}";
            MouseLabel.Text = $"({mousePos.x,4}, {mousePos.y,4}) -> ({mappedPos.x,5}, {mappedPos.y,5})";

            string modStr = FormatModifiers(modifiers);
            var keys = activeKeycodes.Where(k => k != 0).Select(k => $"0x{k:X2}");
            KeyboardLabel.Text = $"{modStr} | [{string.Join(", ", keys)}]";

            UpdateBluetoothIcon(btReady, btDiscovered);
            UpdateCapsLockIcon(capsLockOn);
        }

        public void SetVideoStatus(VideoStatus status)
        {
            _videoStatus = status;

            // Green when actively capturing, gray otherwise (matches macOS tinting).
            VideoIcon.Foreground = status.IsActive()
                ? new SolidColorBrush(Microsoft.UI.Colors.Green)
                : new SolidColorBrush(Microsoft.UI.Colors.Gray);

            // Glyph selection mirrors macOS SF Symbol mapping.
            VideoIcon.Glyph = status switch
            {
                VideoStatus.NoDevice => GlyphQuestion,
                VideoStatus.SingleSourceActive => GlyphVideo,
                VideoStatus.SingleSourcePaused => GlyphPause,
                VideoStatus.MultiSourceActive => GlyphVideoChat,
                VideoStatus.MultiSourcePaused => GlyphQuestion,
                _ => GlyphVideo
            };
        }

        private void UpdateBluetoothIcon(bool btReady, bool btDiscovered)
        {
            if (btReady)
            {
                BluetoothIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
            else if (btDiscovered)
            {
                BluetoothIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
            }
            else
            {
                BluetoothIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
            BluetoothIcon.Glyph = GlyphBluetooth;
        }

        private void UpdateCapsLockIcon(bool capsLockOn)
        {
            CapsLockLabel.Foreground = capsLockOn
                ? new SolidColorBrush(Microsoft.UI.Colors.Green)
                : new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        // ---- Button handling ----

        // Click is the reliable trigger (Button raises it consistently; pointer events
        // are swallowed by the Button control). The toggle/menu logic lives in
        // MainWindow.HandleVideoButtonClick.
        private void OnVideoClick(object sender, RoutedEventArgs e)
        {
            VideoButtonClicked?.Invoke();
        }

        private void OnBluetoothClick(object sender, RoutedEventArgs e)
        {
            BluetoothButtonClicked?.Invoke();
        }


        private static string FormatModifiers(byte modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & 0x01) != 0) parts.Add("LC");
            if ((modifiers & 0x02) != 0) parts.Add("LS");
            if ((modifiers & 0x04) != 0) parts.Add("LA");
            if ((modifiers & 0x08) != 0) parts.Add("LG");
            if ((modifiers & 0x10) != 0) parts.Add("RC");
            if ((modifiers & 0x20) != 0) parts.Add("RS");
            if ((modifiers & 0x40) != 0) parts.Add("RA");
            if ((modifiers & 0x80) != 0) parts.Add("RG");
            return parts.Count == 0 ? "NONE" : string.Join("+", parts);
        }
    }
}