namespace CoKVM.UI.Models
{
    /// <summary>
    /// Video source status. Maps to the macOS StatusBarView.VideoStatus enum.
    /// Reference: control/macos/CoKVM/StatusBarView.swift
    /// </summary>
    public enum VideoStatus
    {
        NoDevice,
        SingleSourceActive,
        SingleSourcePaused,
        MultiSourceActive,
        MultiSourcePaused,
    }

    public static class VideoStatusExtensions
    {
        /// <summary>True when video is actively capturing (used to gate BLE sends).</summary>
        public static bool IsActive(this VideoStatus status) =>
            status == VideoStatus.SingleSourceActive || status == VideoStatus.MultiSourceActive;
    }
}