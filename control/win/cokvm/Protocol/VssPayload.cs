using System.Buffers.Binary;

namespace CoKVM.Protocol
{
    /// <summary>
    /// VSS (Vendor-Specific Service) 22-byte payload structure.
    /// Reference: control/macos/CoKVM/VssPayload.swift
    /// </summary>
    public class VssPayload
    {
        private const byte MagicK = 0x4B; // 'K'
        private const byte MagicM = 0x4D; // 'M'
        private const byte Reserved = 0x00;

        public byte Type { get; }
        private readonly byte[] _data;

        private VssPayload(byte type, byte[] data)
        {
            Type = type;
            _data = data;
        }

        /// <summary>
        /// Create a keyboard payload (Type 0x01).
        /// </summary>
        /// <param name="modifiers">Modifier bitmask (8 bits)</param>
        /// <param name="keycodes">Up to 6 keycodes</param>
        /// <returns>Keyboard payload</returns>
        public static VssPayload Keyboard(byte modifiers, byte[] keycodes)
        {
            var data = new byte[8];
            data[0] = modifiers;
            data[1] = 0x00; // Reserved

            // Copy up to 6 keycodes
            var kcs = new byte[keycodes.Length];
            Array.Copy(keycodes, kcs, Math.Min(keycodes.Length, 6));

            for (int i = 0; i < 6; i++)
            {
                if (i < kcs.Length)
                {
                    data[2 + i] = kcs[i];
                }
                else
                {
                    data[2 + i] = 0x00;
                }
            }

            return new VssPayload(0x01, data);
        }

        /// <summary>
        /// Create a mouse payload (Type 0x02) with ABSOLUTE positions.
        /// </summary>
        /// <param name="buttons">Button bitmask (0x01=left, 0x02=right, 0x04=middle)</param>
        /// <param name="x">X position (0-17280, Int16 little endian)</param>
        /// <param name="y">Y position (0-17280, Int16 little endian)</param>
        /// <param name="wheel">Wheel delta (-120 to +120)</param>
        /// <returns>Mouse payload</returns>
        public static VssPayload Mouse(byte buttons, short x, short y, sbyte wheel)
        {
            var data = new byte[8];
            data[0] = buttons;
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(1), x);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(3), y);
            data[5] = (byte)wheel;
            // data[6-11] are padding, already 0
            return new VssPayload(0x02, data);
        }

        /// <summary>
        /// Create an LED status payload (Type 0x03).
        /// </summary>
        /// <param name="capsLock">Caps Lock state</param>
        /// <returns>LED status payload</returns>
        public static VssPayload LedStatus(bool capsLock)
        {
            var data = new byte[8];
            if (capsLock) data[0] |= 0x02; // Caps Lock
            return new VssPayload(0x03, data);
        }

        /// <summary>
        /// Serialize to 22-byte payload.
        /// Format: [K][M][00][Type][8 bytes data][10 bytes padding]
        /// </summary>
        /// <returns>22-byte payload</returns>
        public byte[] ToPayload()
        {
            var payload = new byte[22];
            payload[0] = MagicK;
            payload[1] = MagicM;
            payload[2] = Reserved;
            payload[3] = Type;

            // Copy data (8 bytes)
            Array.Copy(_data, 0, payload, 4, 8);

            // Padding (bytes 12-21) are already 0

            return payload;
        }
    }
}