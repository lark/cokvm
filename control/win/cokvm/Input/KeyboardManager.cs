using Windows.System;
using CoKVM.Protocol;

namespace CoKVM.Input
{
    /// <summary>
    /// Tracks keyboard modifier state and active keycodes, generating VSS payloads.
    /// Reference: control/macos/CoKVM/KeyboardManager.swift
    /// </summary>
    public class KeyboardManager
    {
        // Modifier bitmask definitions
        public const byte LeftControl = 0x01;
        public const byte LeftShift = 0x02;
        public const byte LeftAlt = 0x04;
        public const byte LeftGui = 0x08;
        public const byte RightControl = 0x10;
        public const byte RightShift = 0x20;
        public const byte RightAlt = 0x40;
        public const byte RightGui = 0x80;

        private byte _modifiers = 0;
        private readonly byte[] _activeKeycodes = new byte[6];

        /// <summary>
        /// Current modifier bitmask.
        /// </summary>
        public byte Modifiers => _modifiers;

        /// <summary>
        /// Current active non-modifier keycodes (6-slot array).
        /// </summary>
        public byte[] ActiveKeycodes => _activeKeycodes;

        /// <summary>
        /// Update the modifier byte from a set of currently-pressed modifier keys.
        /// </summary>
        public void UpdateModifiers(IEnumerable<VirtualKey> pressedModifiers)
        {
            byte mods = 0;
            foreach (var key in pressedModifiers)
            {
                switch (key)
                {
                    case VirtualKey.LeftControl: mods |= LeftControl; break;
                    case VirtualKey.LeftShift: mods |= LeftShift; break;
                    case VirtualKey.LeftMenu: mods |= LeftAlt; break;
                    case VirtualKey.LeftWindows: mods |= LeftGui; break;
                    case VirtualKey.RightControl: mods |= RightControl; break;
                    case VirtualKey.RightShift: mods |= RightShift; break;
                    case VirtualKey.RightMenu: mods |= RightAlt; break;
                    case VirtualKey.RightWindows: mods |= RightGui; break;
                }
            }
            _modifiers = mods;
        }

        /// <summary>
        /// Handle a key-down event.
        /// Modifier keys (Ctrl/Shift/Alt/Win) are NOT added to the keycode array.
        /// Caps Lock is handled elsewhere (LED sync), so callers must
        /// filter it out before calling this.
        /// </summary>
        /// <returns>VSS keyboard payload, or null if the key is unmapped.</returns>
        public VssPayload? OnKeyDown(VirtualKey key)
        {
            if (WindowsKeyMap.IsModifierKey(key))
            {
                return CreatePayload();
            }

            var scancode = WindowsKeyMap.ToHidScancode(key);
            if (scancode == null)
            {
                return null;
            }

            AddKeycode(scancode.Value);
            return CreatePayload();
        }

        /// <summary>
        /// Handle a key-up event.
        /// </summary>
        public VssPayload? OnKeyUp(VirtualKey key)
        {
            if (WindowsKeyMap.IsModifierKey(key))
            {
                return CreatePayload();
            }

            var scancode = WindowsKeyMap.ToHidScancode(key);
            if (scancode == null)
            {
                return null;
            }

            RemoveKeycode(scancode.Value);
            return CreatePayload();
        }

        /// <summary>
        /// Build a keyboard payload for a combo key string like "ctrl+alt+del".
        /// Used by the ComboKeyPanel.
        /// </summary>
        public static (byte modifiers, byte[] keycodes) ParseCombo(string combo)
        {
            byte modifiers = 0;
            var keycodes = new List<byte>();

            var parts = combo.ToLowerInvariant().Split('+');
            foreach (var part in parts)
            {
                switch (part.Trim())
                {
                    case "ctrl": modifiers |= LeftControl; break;
                    case "shift": modifiers |= LeftShift; break;
                    case "alt":
                    case "option": modifiers |= LeftAlt; break;
                    case "cmd":
                    case "command":
                    case "win":
                    case "gui": modifiers |= LeftGui; break;
                    case "f1": keycodes.Add(0x3A); break;
                    case "f2": keycodes.Add(0x3B); break;
                    case "f3": keycodes.Add(0x3C); break;
                    case "f4": keycodes.Add(0x3D); break;
                    case "f5": keycodes.Add(0x3E); break;
                    case "f6": keycodes.Add(0x3F); break;
                    case "f7": keycodes.Add(0x40); break;
                    case "f8": keycodes.Add(0x41); break;
                    case "f9": keycodes.Add(0x42); break;
                    case "f10": keycodes.Add(0x43); break;
                    case "f11": keycodes.Add(0x44); break;
                    case "f12": keycodes.Add(0x45); break;
                    case "del":
                    case "delete": keycodes.Add(0x4C); break;
                    case "ins":
                    case "insert": keycodes.Add(0x49); break;
                    case "home": keycodes.Add(0x4A); break;
                    case "end": keycodes.Add(0x4D); break;
                    case "pgup":
                    case "pageup": keycodes.Add(0x4B); break;
                    case "pgdn":
                    case "pagedn":
                    case "pagedown": keycodes.Add(0x4E); break;
                    case "prtsc":
                    case "printscreen": keycodes.Add(0x46); break;
                    case "scrlk":
                    case "scrolllock": keycodes.Add(0x47); break;
                    case "pause": keycodes.Add(0x48); break;
                    case "esc":
                    case "escape": keycodes.Add(0x29); break;
                    case "tab": keycodes.Add(0x2B); break;
                    case "enter":
                    case "return": keycodes.Add(0x28); break;
                    case "space": keycodes.Add(0x2C); break;
                    case "backspace": keycodes.Add(0x2A); break;
                    case "up": keycodes.Add(0x52); break;
                    case "down": keycodes.Add(0x51); break;
                    case "left": keycodes.Add(0x50); break;
                    case "right": keycodes.Add(0x4F); break;
                }
            }

            return (modifiers, keycodes.ToArray());
        }

        private void AddKeycode(byte scancode)
        {
            // Auto-repeat fires KeyDown repeatedly while a key is held. Ignore the
            // duplicates so a single held key occupies exactly one slot — otherwise
            // repeats fill all 6 slots with the same scancode and a single KeyUp
            // (which removes only one) leaves the rest "stuck".
            for (int i = 0; i < _activeKeycodes.Length; i++)
            {
                if (_activeKeycodes[i] == scancode)
                {
                    return;
                }
            }

            for (int i = 0; i < _activeKeycodes.Length; i++)
            {
                if (_activeKeycodes[i] == 0)
                {
                    _activeKeycodes[i] = scancode;
                    return;
                }
            }
        }

        private void RemoveKeycode(byte scancode)
        {
            for (int i = 0; i < _activeKeycodes.Length; i++)
            {
                if (_activeKeycodes[i] == scancode)
                {
                    _activeKeycodes[i] = 0;
                    break;
                }
            }
        }

        private VssPayload CreatePayload()
        {
            var copy = new byte[_activeKeycodes.Length];
            Array.Copy(_activeKeycodes, copy, _activeKeycodes.Length);
            return VssPayload.Keyboard(_modifiers, copy);
        }
    }
}