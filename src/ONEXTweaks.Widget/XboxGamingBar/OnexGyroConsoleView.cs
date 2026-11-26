// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
    internal sealed class OnexGyroConsoleView
    {
        internal Grid View { get; } = new Grid { Height = 180 };
        internal int FrameCount { get; private set; }
        internal Quaternion Presented { get; private set; } = Quaternion.Identity;
        internal double RenderMilliseconds { get; private set; }

        private readonly WriteableBitmap bitmap = new WriteableBitmap(OnexGyroConsoleMesh.Width, OnexGyroConsoleMesh.Height);
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Task<OnexGyroConsoleMesh> loading;
        private readonly OnexApprovedTheme palette;
        private OnexGyroConsoleMesh mesh;
        private Quaternion target = Quaternion.Identity, smoothed = Quaternion.Identity;
        private bool attached, busy, forcePaint = true, failed;
        private int recenterRevision;
        private double lastTick;

        internal OnexGyroConsoleView(OnexApprovedTheme theme = null)
        {
            palette = theme ?? OnexApprovedTheme.Current;
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            AutomationProperties.SetName(image, OnexStrings.Get("ONEXPLAYER 3 motion model"));
            View.Children.Add(image);
            var message = new TextBlock
            {
                Text = OnexStrings.Get("The 3D model could not load."),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            View.Children.Add(message);
            timer.Tick += Tick;
            View.Loaded += async (sender, args) =>
            {
                attached = true;
                try
                {
                    if (loading == null) loading = LoadAsync();
                    mesh = await loading;
                    if (attached)
                    {
                        lastTick = clock.Elapsed.TotalSeconds;
                        timer.Start();
                    }
                }
                catch
                {
                    failed = true;
                    message.Visibility = Visibility.Visible;
                }
            };
            View.Unloaded += (sender, args) =>
            {
                attached = false;
                timer.Stop();
            };
        }

        private async Task<OnexGyroConsoleMesh> LoadAsync()
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/OnexGyroConsole.bin"));
            var buffer = await FileIO.ReadBufferAsync(file);
            byte[] data = buffer.ToArray();
            return await Task.Run(() => new OnexGyroConsoleMesh(new System.IO.MemoryStream(data, false), palette));
        }

        internal void SetPose(Quaternion value) => target = value;

        internal void Recenter()
        {
            recenterRevision++;
            target = smoothed = Quaternion.Identity;
            forcePaint = true;
        }

        private async void Tick(object sender, object args)
        {
            if (!attached || busy || failed || mesh == null) return;
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = Math.Max(0, Math.Min(.1, now - lastTick));
            lastTick = now;
            smoothed = Quaternion.Slerp(smoothed, target, (float)(1 - Math.Exp(-elapsed * 24)));
            if (!forcePaint && Math.Abs(Quaternion.Dot(smoothed, Presented)) > .999999f) return;
            Quaternion snapshot = smoothed;
            int revision = recenterRevision;
            busy = true;
            try
            {
                double ms = await Task.Run(() =>
                {
                    long started = Stopwatch.GetTimestamp();
                    mesh.Render(snapshot);
                    return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                });
                if (!attached || revision != recenterRevision) return;
                using (var stream = bitmap.PixelBuffer.AsStream())
                    stream.Write(mesh.Pixels, 0, mesh.Pixels.Length);
                bitmap.Invalidate();
                Presented = snapshot;
                RenderMilliseconds = ms;
                FrameCount++;
                forcePaint = false;
            }
            finally
            {
                busy = false;
            }
        }
    }
}
