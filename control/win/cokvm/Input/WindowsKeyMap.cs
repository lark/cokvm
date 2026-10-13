using Windows.System;

namespace CoKVM.Input
{
    /// <summary>
    /// Maps Windows VirtualKey values to USB HID usage IDs (scancodes).
    /// Reference: control/macos/CoKVM/KeyCodeMap.swift
    /// </summary>
    public static class WindowsKeyMap
    {
        // Modifier key VirtualKeys that should NOT be sent as keycodes in the array.
        // They are reported via the modifier byte instead.
        // Reference: control/macos/CoKVM/KeyCodeMap.swift (macosModifierKeycodes)
        public static readonly HashSet<VirtualKey> ModifierKeys = new()
        {
            VirtualKey.LeftControl,
            VirtualKey.RightControl,
            VirtualKey.LeftShift,
            VirtualKey.RightShift,
            VirtualKey.LeftMenu,
            VirtualKey.RightMenu,
            VirtualKey.LeftWindows,
            VirtualKey.RightWindows,
            VirtualKey.CapitalLock, // Caps Lock
        };

        private static readonly Dictionary<VirtualKey, byte> KeyMap = new()
        {
            // Num Lock is forwarded as a normal key; the controlled host toggles its own state.
            { VirtualKey.NumberKeyLock, 0x53 },

            // Letters A-Z (HID 0x04-0x1D)
            { VirtualKey.A, 0x04 },
            { VirtualKey.B, 0x05 },
            { VirtualKey.C, 0x06 },
            { VirtualKey.D, 0x07 },
            { VirtualKey.E, 0x08 },
            { VirtualKey.F, 0x09 },
            { VirtualKey.G, 0x0A },
            { VirtualKey.H, 0x0B },
            { VirtualKey.I, 0x0C },
            { VirtualKey.J, 0x0D },
            { VirtualKey.K, 0x0E },
            { VirtualKey.L, 0x0F },
            { VirtualKey.M, 0x10 },
            { VirtualKey.N, 0x11 },
            { VirtualKey.O, 0x12 },
            { VirtualKey.P, 0x13 },
            { VirtualKey.Q, 0x14 },
            { VirtualKey.R, 0x15 },
            { VirtualKey.S, 0x16 },
            { VirtualKey.T, 0x17 },
            { VirtualKey.U, 0x18 },
            { VirtualKey.V, 0x19 },
            { VirtualKey.W, 0x1A },
            { VirtualKey.X, 0x1B },
            { VirtualKey.Y, 0x1C },
            { VirtualKey.Z, 0x1D },

            // Numbers 1-0 (top row, HID 0x1E-0x27)
            { VirtualKey.Number1, 0x1E },
            { VirtualKey.Number2, 0x1F },
            { VirtualKey.Number3, 0x20 },
            { VirtualKey.Number4, 0x21 },
            { VirtualKey.Number5, 0x22 },
            { VirtualKey.Number6, 0x23 },
            { VirtualKey.Number7, 0x24 },
            { VirtualKey.Number8, 0x25 },
            { VirtualKey.Number9, 0x26 },
            { VirtualKey.Number0, 0x27 },

            // Punctuation. The WinRT VirtualKey enum has no named Oem* members,
            // so we use the integer Windows VK_ codes (they share the enum's
            // underlying int values).
            // VK_OEM_MINUS = 0xBD (top-row -)
            { (VirtualKey)0xBD, 0x2D },
            // VK_OEM_PLUS = 0xBB (top-row =)
            { (VirtualKey)0xBB, 0x2E },
            // VK_OEM_4 = 0xDB ([), VK_OEM_6 = 0xDD (]), VK_OEM_5 = 0xDC (\)
            { (VirtualKey)0xDB, 0x2F },
            { (VirtualKey)0xDD, 0x30 },
            { (VirtualKey)0xDC, 0x31 },
            // VK_OEM_1 = 0xBA (;), VK_OEM_7 = 0xDE (')
            { (VirtualKey)0xBA, 0x33 },
            { (VirtualKey)0xDE, 0x34 },
            // VK_OEM_COMMA = 0xBC (,), VK_OEM_PERIOD = 0xBE (.), VK_OEM_2 = 0xBF (/)
            { (VirtualKey)0xBC, 0x36 },
            { (VirtualKey)0xBE, 0x37 },
            { (VirtualKey)0xBF, 0x38 },
            // VK_OEM_3 = 0xC0 (` backtick)
            { (VirtualKey)0xC0, 0x35 },

            // Special keys
            { VirtualKey.Enter, 0x28 },
            { VirtualKey.Escape, 0x29 },
            { VirtualKey.Back, 0x2A },         // Backspace / Delete on Mac
            { VirtualKey.Tab, 0x2B },
            { VirtualKey.Space, 0x2C },

            // Function keys F1-F12
            { VirtualKey.F1, 0x3A },
            { VirtualKey.F2, 0x3B },
            { VirtualKey.F3, 0x3C },
            { VirtualKey.F4, 0x3D },
            { VirtualKey.F5, 0x3E },
            { VirtualKey.F6, 0x3F },
            { VirtualKey.F7, 0x40 },
            { VirtualKey.F8, 0x41 },
            { VirtualKey.F9, 0x42 },
            { VirtualKey.F10, 0x43 },
            { VirtualKey.F11, 0x44 },
            { VirtualKey.F12, 0x45 },

            // Navigation keys
            { VirtualKey.Print, 0x46 },        // PrintScreen
            { VirtualKey.Scroll, 0x47 },       // ScrollLock
            { VirtualKey.Pause, 0x48 },        // Pause
            { VirtualKey.Insert, 0x49 },
            { VirtualKey.Home, 0x4A },
            { VirtualKey.PageUp, 0x4B },
            { VirtualKey.Delete, 0x4C },
            { VirtualKey.End, 0x4D },
            { VirtualKey.PageDown, 0x4E },

            // Arrow keys
            { VirtualKey.Right, 0x4F },
            { VirtualKey.Left, 0x50 },
            { VirtualKey.Down, 0x51 },
            { VirtualKey.Up, 0x52 },
        };

        /// <summary>
        /// Convert a Windows VirtualKey to a HID scancode.
        /// Returns null if the key is not mapped.
        /// </summary>
        public static byte? ToHidScancode(VirtualKey virtualKey)
        {
            return KeyMap.TryGetValue(virtualKey, out byte scancode) ? scancode : null;
        }

        /// <summary>
        /// Returns true if the key is a modifier key (handled via modifier byte, not as keycode).
        /// </summary>
        public static bool IsModifierKey(VirtualKey virtualKey)
        {
            return ModifierKeys.Contains(virtualKey);
        }
    }
}