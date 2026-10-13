using CoKVM.Protocol;

namespace CoKVM.Input
{
    /// <summary>
    /// Tracks mouse position and converts window coordinates to HID absolute
    /// coordinates (0-17280). Windows Y-axis is top-to-bottom, so NO inversion
    /// is needed (unlike macOS which inverts with `height - y`).
    /// Reference: control/macos/CoKVM/MouseManager.swift
    /// </summary>
    public class MouseManager
    {
        private const double HidRange = 17280.0;

        public (int X, int Y) LastWinPos { get; private set; } = (0, 0);
        public (short X, short Y) LastMappedPos { get; private set; } = (0, 0);
        public byte LastButtons { get; private set; }

        /// <summary>
        /// Handle a mouse move / button change event.
        /// Produces a mouse payload with ABSOLUTE coordinates.
        /// </summary>
        /// <param name="x">Pointer X within the video view (top-left origin)</param>
        /// <param name="y">Pointer Y within the video view (top-left origin)</param>
        /// <param name="width">Video view width</param>
        /// <param name="height">Video view height</param>
        /// <param name="buttons">Button bitmask (0x01=left, 0x02=right, 0x04=middle)</param>
        public VssPayload OnMouseMove(double x, double y, double width, double height, byte buttons)
        {
            var hidPos = ConvertToHid(x, y, width, height);

            LastWinPos = ((int)x, (int)y);
            LastMappedPos = hidPos;
            LastButtons = buttons;

            return VssPayload.Mouse(buttons, hidPos.X, hidPos.Y, 0);
        }

        /// <summary>
        /// Handle a mouse wheel event.
        /// </summary>
        /// <param name="deltaY">Scroll delta (typically multiples of 120)</param>
        public VssPayload OnMouseWheel(double deltaY)
        {
            // Clamp to Int8 range (-128..127), scaled the same way as macOS (deltaY * 120)
            int scaled = (int)(deltaY * 120);
            sbyte wheel = (sbyte)Math.Clamp(scaled, sbyte.MinValue, sbyte.MaxValue);
            // X/Y are absolute: repeat the last position and buttons instead of (0, 0),
            // otherwise the pointer jumps to the origin and held buttons are released.
            return VssPayload.Mouse(LastButtons, LastMappedPos.X, LastMappedPos.Y, wheel);
        }

        /// <summary>
        /// Convert window coordinates to HID absolute coordinates.
        /// Windows Y-axis is top-to-bottom, so no inversion (unlike macOS).
        /// </summary>
        public static (short X, short Y) ConvertToHid(double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return (0, 0);
            }

            double hidX = x / width * HidRange;
            // Windows: y is already top-to-bottom, no inversion needed.
            double hidY = y / height * HidRange;

            short clampedX = (short)Math.Clamp((int)hidX, 0, 17280);
            short clampedY = (short)Math.Clamp((int)hidY, 0, 17280);

            return (clampedX, clampedY);
        }
    }
}