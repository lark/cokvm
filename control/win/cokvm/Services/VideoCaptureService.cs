using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace CoKVM.Services
{
    /// <summary>
    /// Video capture from USB capture devices (e.g. MS2130/MS2130S) using MediaCapture.
    /// Exposes a GPU-accelerated MediaSource (via MediaFrameReader) for MediaPlayerElement.
    /// Format selection prefers 1920x1080@30fps NV12, falling back to 1280x720 or other.
    /// Reference: control/macos/CoKVM/VideoCapture.swift
    /// </summary>
    public class VideoCaptureService : IVideoCaptureService
    {
        private readonly IVideoCaptureDelegate _delegate;
        private MediaCapture? _mediaCapture;
        private MediaSource? _mediaSource;
        private DeviceWatcher? _watcher;
        private List<VideoCaptureDeviceInfo> _devices = new();
        private VideoCaptureDeviceInfo? _currentDevice;
        private string _currentFormat = "N/A";

        public bool IsReady { get; private set; }
        public string CurrentFormat => _currentFormat;
        public IReadOnlyList<VideoCaptureDeviceInfo> AvailableDevices => _devices;

        public VideoCaptureService(IVideoCaptureDelegate @delegate)
        {
            _delegate = @delegate;
            SetupDeviceWatcher();
        }

        private void SetupDeviceWatcher()
        {
            _watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            _watcher.Added += OnDeviceAdded;
            _watcher.Removed += OnDeviceRemoved;
            _watcher.Updated += OnDeviceUpdated;
            _watcher.Start();
        }

        private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
        {
            _ = RefreshDevicesAsync();
        }

        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            _ = RefreshDevicesAsync();
        }

        private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            // No-op
        }

        public async Task RefreshDevicesAsync()
        {
            var found = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            _devices = found.Select(d => new VideoCaptureDeviceInfo(d.Id, d.Name, string.Empty)).ToList();
            _delegate.OnDevicesChanged(_devices);

            // If the active device was removed, stop.
            if (IsReady && _currentDevice != null && !_devices.Contains(_currentDevice.Value))
            {
                Stop();
            }
        }

        public IReadOnlyList<VideoCaptureDeviceInfo> GetAvailableDevices()
        {
            return _devices;
        }

        public async void Start(VideoCaptureDeviceInfo? device = null)
        {
            await RefreshDevicesAsync();
            if (_devices.Count == 0)
            {
                _delegate.OnVideoReady(false, "No device");
                return;
            }

            var selected = device ?? _devices[0];
            _currentDevice = selected;

            try
            {
                _mediaCapture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = selected.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    // Hardware-accelerated decoding via Media Foundation (keeps CPU low).
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                };
                await _mediaCapture.InitializeAsync(settings);

                await SelectBestFormatAsync(_mediaCapture);

                // Build a GPU-accelerated preview via MediaSource bound to a
                // MediaFrameSource (CaptureElement is not available in WinUI 3 desktop).
                StartPreviewSource(_mediaCapture);

                IsReady = true;
                _delegate.OnVideoReady(true, _currentFormat);
            }
            catch (Exception ex)
            {
                IsReady = false;
                _delegate.OnVideoReady(false, "Error: " + ex.Message);
            }
        }

        private void StartPreviewSource(MediaCapture capture)
        {
            // Pick a video frame source (prefer the preview stream).
            MediaFrameSource? frameSource = null;
            foreach (var kvp in capture.FrameSources)
            {
                if (kvp.Value.Info.MediaStreamType == MediaStreamType.VideoPreview)
                {
                    frameSource = kvp.Value;
                    break;
                }
            }
            frameSource ??= capture.FrameSources.Values.FirstOrDefault();

            if (frameSource == null)
            {
                throw new InvalidOperationException("No video frame source available");
            }

            _mediaSource = MediaSource.CreateFromMediaFrameSource(frameSource);
        }

        public MediaSource? GetMediaSource() => _mediaSource;

        public void Stop()
        {
            try { _mediaSource?.Dispose(); } catch { }
            _mediaSource = null;

            try { _mediaCapture?.Dispose(); }
            catch { }
            _mediaCapture = null;
            IsReady = false;
            _currentFormat = "Stopped";
            _delegate.OnVideoReady(false, "Stopped");
        }

        public MediaCapture? GetMediaCapture() => _mediaCapture;

        /// <summary>
        /// Pick the best available format: prefer 1920x1080@30 NV12, then 1280x720@30 NV12,
        /// then by resolution/fps/pixel-format score.
        /// Reference: VideoCapture.swift selectBestFormat / formatDescription.
        /// </summary>
        private async Task SelectBestFormatAsync(MediaCapture capture)
        {
            string formatString = "N/A";
            var streamProperties = capture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview);
            if (streamProperties == null || streamProperties.Count == 0)
            {
                _currentFormat = formatString;
                return;
            }

            VideoEncodingProperties? best = null;
            int bestScore = 0;

            foreach (var prop in streamProperties)
            {
                if (prop is not VideoEncodingProperties video) continue;

                uint width = video.Width;
                uint height = video.Height;
                uint fps = video.FrameRate?.Numerator ?? 0;
                string subtype = video.Subtype ?? string.Empty;

                int score = 0;

                if (width == 1920 && height == 1080) score += 100;
                else if (width == 1280 && height == 720) score += 50;

                // NV12 is preferred (MediaFoundation HW-friendly), then YUY2, then others.
                if (subtype.Equals("NV12", StringComparison.OrdinalIgnoreCase)) score += 50;
                else if (subtype.Equals("YUY2", StringComparison.OrdinalIgnoreCase)) score += 40;
                else if (subtype.Equals("YUV420", StringComparison.OrdinalIgnoreCase) ||
                         subtype.Equals("I420", StringComparison.OrdinalIgnoreCase)) score += 25;

                if (fps >= 30) score += 30;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = video;
                }
            }

            if (best != null)
            {
                try
                {
                    await capture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, best);
                }
                catch { }

                string fmtName = best.Subtype?.ToUpperInvariant() ?? "?";
                uint f = best.FrameRate?.Numerator ?? 0;
                string fpsStr = f > 0 ? $"{f}fps" : "?";
                formatString = $"{best.Width}x{best.Height} {fmtName} {fpsStr}";
            }

            _currentFormat = formatString;
        }
    }
}