using Windows.Media.Core;

namespace CoKVM.Services
{
    public readonly record struct VideoCaptureDeviceInfo(
        string Id,
        string Name,
        string EnclosureId);

    public interface IVideoCaptureDelegate
    {
        void OnVideoReady(bool ready, string format);
        void OnDevicesChanged(IReadOnlyList<VideoCaptureDeviceInfo> devices);
    }

    /// <summary>
    /// Captures video from a USB capture device and exposes a GPU-accelerated
    /// <see cref="MediaSource"/> (backed by a MediaFrameReader) for rendering via
    /// MediaPlayerElement. CPU usage target: &lt; 10%.
    /// </summary>
    public interface IVideoCaptureService
    {
        bool IsReady { get; }
        string CurrentFormat { get; }
        IReadOnlyList<VideoCaptureDeviceInfo> AvailableDevices { get; }

        void Start(VideoCaptureDeviceInfo? device = null);
        void Stop();

        /// <summary>
        /// MediaSource for the active capture, for binding to MediaPlayerElement.Source.
        /// Null when not capturing.
        /// </summary>
        MediaSource? GetMediaSource();
    }
}