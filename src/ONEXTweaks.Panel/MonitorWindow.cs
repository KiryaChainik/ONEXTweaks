using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ONEXTweaks.Backend;

namespace ONEXTweaks.Panel;

internal sealed class MonitorWindow : Window
{
    private static readonly Brush BackgroundBrush = Brush("#111820");
    private static readonly Brush SurfaceBrush = Brush("#1B2631");
    private static readonly Brush TextBrush = Brush("#F1F4F8");
    private static readonly Brush MutedBrush = Brush("#9BAAB8");
    private static readonly Brush AccentBrush = Brush("#B9EE79");
    private readonly TextBlock status = Text("Подключение…", 13, MutedBrush);
    private readonly TextBlock cpu = Text("—", 26, TextBrush);
    private readonly TextBlock memory = Text("—", 26, TextBrush);
    private readonly TextBlock battery = Text("—", 26, TextBrush);
    private readonly TextBlock gpu = Text("—", 26, TextBrush);
    private readonly TextBlock powerSource = Text("—", 13, MutedBrush);
    private readonly TextBlock gpuName = Text("Arc B390", 13, MutedBrush);
    private readonly TextBlock cpuDetail = Text("загрузка", 13, MutedBrush);
    private readonly TextBlock appliedPower = Text("Подключение управления…", 14, MutedBrush);
    private readonly TextBlock powerMessage = Text("", 12, MutedBrush);
    private readonly TextBlock pl1Label = Text("—", 14, TextBrush);
    private readonly TextBlock pl2Label = Text("—", 14, TextBrush);
    private readonly Slider pl1 = new() { Minimum = 8, Maximum = 40, TickFrequency = 1, IsSnapToTickEnabled = true, Height = 28, IsEnabled = false };
    private readonly Slider pl2 = new() { Minimum = 8, Maximum = 41, TickFrequency = 1, IsSnapToTickEnabled = true, Height = 28, IsEnabled = false };
    private readonly CheckBox linkBoost = new() { Content = "PL2 = PL1 + 1 Вт", IsChecked = true, Foreground = MutedBrush, FontSize = 12, Margin = new Thickness(0, 4, 0, 12) };
    private readonly System.Windows.Controls.Button apply = new() { Content = "Применить", MinHeight = 44, IsEnabled = false, FontSize = 14 };
    private readonly System.Windows.Controls.Button restore = new() { Content = "Вернуть исходные", MinHeight = 44, IsEnabled = false, FontSize = 13 };
    private readonly PowerChannel power;
    private readonly bool exercisePower;
    private PowerState? powerState;
    private bool syncingPower, dirtyPower, applyingPower;
    private object? powerTest;
    private readonly Border root;
    private readonly string? verifyDirectory;
    private HwndSource? source;
    private IntPtr previousForeground;
    private bool hotkeyRegistered;
    private int snapshots;
    private bool captured;
    private DateTimeOffset lastUpdate;
    private readonly DispatcherTimer freshness;

    internal MonitorWindow(string? verify, bool exercisePower, PowerChannel power)
    {
        verifyDirectory = verify;
        this.exercisePower = exercisePower;
        this.power = power;
        Title = "ONEXTweaks — мощность и мониторинг";
        Width = 460; Height = Math.Min(840, SystemParameters.WorkArea.Height - 32); ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = true; FontFamily = new FontFamily("Segoe UI");
        previousForeground = GetForegroundWindow();
        Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 28);
        Top = Math.Max(20, (SystemParameters.WorkArea.Height - Height) / 2);
        root = new Border { Background = BackgroundBrush, CornerRadius = new CornerRadius(18), Padding = new Thickness(24), BorderBrush = Brush("#354251"), BorderThickness = new Thickness(1) };
        Content = root;
        var layout = new StackPanel();
        root.Child = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
        var header = new DockPanel { LastChildFill = true };
        var close = Button("×", () => Close()); close.ToolTip = "Завершить ONEXTweaks";
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var hide = Button("−", () => HidePanel()); hide.ToolTip = "Скрыть панель";
        DockPanel.SetDock(hide, Dock.Right); header.Children.Add(hide);
        header.Children.Add(Text("ONEXTweaks", 24, TextBrush, true));
        layout.Children.Add(header);
        layout.Children.Add(Text("ONEXPLAYER 3  ·  G3 Extreme", 13, MutedBrush));
        status.Margin = new Thickness(0, 22, 0, 12); layout.Children.Add(status);
        layout.Children.Add(CreatePowerSection());
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        AddCard(grid, 0, 0, "ПРОЦЕССОР", cpu, cpuDetail);
        AddCard(grid, 0, 1, "ВИДЕОЯДРО", gpu, gpuName);
        AddCard(grid, 1, 0, "ПАМЯТЬ", memory, Text("использовано, ГБ", 13, MutedBrush));
        AddCard(grid, 1, 1, "БАТАРЕЯ", battery, powerSource);
        layout.Children.Add(grid);
        var footer = Text("Ctrl + Alt + F10 — открыть / скрыть  ·  Esc — скрыть", 11, MutedBrush);
        footer.Margin = new Thickness(4, 12, 0, 0); layout.Children.Add(footer);
        var version = Text("0.3  ·  Мощность и мониторинг", 11, MutedBrush);
        version.Margin = new Thickness(4, 8, 0, 0); layout.Children.Add(version);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) DragMove(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { HidePanel(); e.Handled = true; } };
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) ?? throw new InvalidOperationException("No window handle");
            source.AddHook(Hook);
            hotkeyRegistered = RegisterHotKey(source.Handle, 1, 0x0001 | 0x0002 | 0x4000, 0x79);
            if (!hotkeyRegistered) footer.Text = "Esc — скрыть панель\nГорячая клавиша уже занята";
        };
        freshness = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        freshness.Tick += (_, _) => { if (lastUpdate != default && DateTimeOffset.UtcNow - lastUpdate > TimeSpan.FromSeconds(4)) ConnectionFailed("stale"); };
        freshness.Start();
    }

    internal void Update(TelemetrySnapshot snapshot)
    {
        lastUpdate = snapshot.TimestampUtc;
        status.Text = "●  Живые данные"; status.Foreground = AccentBrush;
        cpu.Text = snapshot.Windows.CpuLoadPercent?.ToString("F0", CultureInfo.InvariantCulture) + "%";
        if (snapshot.Windows.CpuLoadPercent is null) cpu.Text = "—";
        memory.Text = snapshot.Windows.MemoryUsedBytes is { } bytes ? (bytes / 1073741824.0).ToString("F1", CultureInfo.InvariantCulture) : "—";
        battery.Text = snapshot.Windows.BatteryPercent is { } percent ? percent + "%" : "—";
        powerSource.Text = snapshot.Windows.OnAcPower switch { true => "от сети", false => "от батареи", null => "питание неизвестно" };
        var adapter = snapshot.Intel.Adapters.FirstOrDefault(a => a.DeviceType == 1);
        double? clock = adapter?.Frequencies.FirstOrDefault(f => f.Type == 0 && f.Status == "ok")?.ActualMHz;
        gpu.Text = clock is { } mhz ? mhz.ToString("F0", CultureInfo.InvariantCulture) : "—";
        gpuName.Text = clock is not null ? "Arc B390 · МГц" : "частота недоступна";
        snapshots++;
        if (!captured && verifyDirectory is not null && snapshots >= 3)
        {
            captured = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(async () => await VerifyAndCapture(snapshot)));
        }
    }

    private Border CreatePowerSection()
    {
        var content = new StackPanel { Margin = new Thickness(16) };
        var heading = PowerHeading("МОЩНОСТЬ", appliedPower); heading.Margin = new Thickness(0, 0, 0, 14); content.Children.Add(heading);
        content.Children.Add(PowerHeading("PL1 / TDP · 8–40 Вт", pl1Label)); content.Children.Add(pl1);
        content.Children.Add(PowerHeading("PL2 / BOOST · до 41 Вт", pl2Label)); content.Children.Add(pl2);
        content.Children.Add(linkBoost);
        var buttons = new Grid(); buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new());
        apply.Margin = new Thickness(0, 0, 5, 0); apply.Background = AccentBrush; apply.Foreground = BackgroundBrush;
        restore.Margin = new Thickness(5, 0, 0, 0); restore.Background = SurfaceBrush; restore.Foreground = TextBrush;
        Grid.SetColumn(restore, 1); buttons.Children.Add(apply); buttons.Children.Add(restore); content.Children.Add(buttons);
        powerMessage.Margin = new Thickness(0, 10, 0, 0); powerMessage.TextWrapping = TextWrapping.Wrap; content.Children.Add(powerMessage);
        pl1.ValueChanged += (_, _) =>
        {
            pl1Label.Text = $"{pl1.Value:F0} Вт";
            if (linkBoost.IsChecked == true) pl2.Value = Math.Min(41, pl1.Value + 1);
            if (!syncingPower) dirtyPower = true;
            RefreshPowerControls();
        };
        pl2.ValueChanged += (_, _) => { pl2Label.Text = $"{pl2.Value:F0} Вт"; if (!syncingPower) dirtyPower = true; RefreshPowerControls(); };
        linkBoost.Checked += (_, _) => { pl2.Value = Math.Min(41, pl1.Value + 1); RefreshPowerControls(); };
        linkBoost.Unchecked += (_, _) => RefreshPowerControls();
        apply.Click += async (_, _) => { try { await ApplyRequestedPower(); } catch (Exception ex) { PowerFailed(ex.Message); } };
        restore.Click += async (_, _) => { try { await RestorePower(); } catch (Exception ex) { PowerFailed(ex.Message); } };
        return new Border { Background = SurfaceBrush, CornerRadius = new CornerRadius(12), Margin = new Thickness(4, 0, 4, 12), Child = content };
    }
    private static DockPanel PowerHeading(string title, TextBlock value)
    {
        var row = new DockPanel(); DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Text(title, 11, MutedBrush, true)); return row;
    }
    internal void UpdatePower(PowerState state)
    {
        powerState = state;
        appliedPower.Text = $"PL1 {state.Pl1Watts:0.###} Вт  ·  PL2 {state.Pl2Watts:0.###} Вт";
        appliedPower.Foreground = state.CanWrite ? AccentBrush : MutedBrush;
        if (state.CpuTemperatureC is { } temperature) cpuDetail.Text = $"{temperature:F0} °C · загрузка";
        if (!dirtyPower && !applyingPower && state.CanWrite)
        {
            SyncPowerSliders(state);
        }
        if (string.IsNullOrEmpty(powerMessage.Text)) powerMessage.Text = state.CanWrite ? "Фактические лимиты устройства." : state.Status;
        RefreshPowerControls();
    }
    private void SyncPowerSliders(PowerState state)
    {
        syncingPower = true;
        pl1.Value = state.Pl1Watts; pl2.Value = state.Pl2Watts;
        syncingPower = false;
    }
    private void RefreshPowerControls()
    {
        bool ready = powerState?.CanWrite == true && !applyingPower;
        pl1.IsEnabled = ready; pl2.IsEnabled = ready && linkBoost.IsChecked != true; linkBoost.IsEnabled = ready;
        apply.IsEnabled = ready && pl2.Value >= pl1.Value;
        restore.IsEnabled = ready;
    }
    internal void PowerFailed(string message)
    {
        powerMessage.Text = message;
        if (powerState is not null) powerState = powerState with { CanWrite = false };
        RefreshPowerControls();
    }
    private async Task<PowerState> ApplyRequestedPower()
    {
        applyingPower = true; RefreshPowerControls(); powerMessage.Text = "Применение и проверка…";
        try
        {
            var reply = await power.SetAsync((int)pl1.Value, (int)pl2.Value);
            if (reply.State is not null) UpdatePower(reply.State);
            if (!reply.Ok) throw new InvalidOperationException(reply.Error);
            dirtyPower = false;
            if (reply.State is not null) SyncPowerSliders(reply.State);
            powerMessage.Text = "Применено. Значения проверены.";
            return reply.State ?? throw new InvalidDataException("Нет подтверждения лимитов");
        }
        finally { applyingPower = false; RefreshPowerControls(); }
    }
    private async Task<PowerState> RestorePower()
    {
        applyingPower = true; RefreshPowerControls();
        try
        {
            var reply = await power.RestoreAsync();
            if (reply.State is not null) UpdatePower(reply.State);
            if (!reply.Ok) throw new InvalidOperationException(reply.Error);
            if (reply.State is not null) SyncPowerSliders(reply.State);
            dirtyPower = false; powerMessage.Text = "Исходные лимиты восстановлены.";
            return reply.State ?? throw new InvalidDataException("Нет подтверждения восстановления");
        }
        finally { applyingPower = false; RefreshPowerControls(); }
    }
    private async Task VerifyAndCapture(TelemetrySnapshot snapshot)
    {
        try
        {
            if (exercisePower)
            {
                for (int i = 0; i < 100 && powerState?.CanWrite != true; i++) await Task.Delay(100);
                var before = powerState ?? throw new InvalidOperationException("Нет аппаратного модуля");
                if (!before.CanWrite) throw new InvalidOperationException("Управление недоступно: " + powerMessage.Text);
                PowerState? applied = null;
                PowerState? restored = null;
                try
                {
                    pl1.Value = before.Pl1Watts - 1; pl2.Value = before.Pl2Watts - 1;
                    applied = await ApplyRequestedPower();
                    if (applied.Pl1Watts != before.Pl1Watts - 1 || applied.Pl2Watts != before.Pl2Watts - 1) throw new InvalidDataException("UI power verification failed");
                }
                finally { restored = await RestorePower(); }
                if (restored.RawLimits != before.RawLimits) throw new InvalidDataException("Original limit register was not restored");
                powerTest = new { before, applied, restored, rawRestored = true };
            }
            CaptureAndExit(snapshot);
        }
        catch (Exception ex) { ConnectionFailed(ex.Message); }
    }

    internal void ConnectionFailed(string detail)
    {
        status.Text = "Нет связи с backend"; status.Foreground = MutedBrush;
        cpu.Text = memory.Text = battery.Text = gpu.Text = "—";
        if (verifyDirectory is not null)
        {
            Directory.CreateDirectory(verifyDirectory);
            File.WriteAllText(Path.Combine(verifyDirectory, "panel-error.txt"), detail);
            Application.Current.Shutdown(1);
        }
    }

    private void CaptureAndExit(TelemetrySnapshot snapshot)
    {
        Directory.CreateDirectory(verifyDirectory!);
        root.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(verifyDirectory!, "quick-panel.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(verifyDirectory!, "panel-state.json"), JsonSerializer.Serialize(snapshot, Backend.Program.Json));
        File.WriteAllText(Path.Combine(verifyDirectory!, "panel-verification.json"), JsonSerializer.Serialize(new
        {
            hotkeyRegistered,
            sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId,
            samplesReceived = snapshots,
            verifiedAtUtc = DateTimeOffset.UtcNow,
            hardwareWritesImplemented = powerState?.CanWrite == true,
            power = powerState,
            powerTest
        }, Backend.Program.Json));
        Application.Current.Shutdown();
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 1)
        {
            if (IsVisible) HidePanel();
            else { previousForeground = GetForegroundWindow(); Show(); Activate(); }
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void HidePanel() { Hide(); if (previousForeground != IntPtr.Zero) SetForegroundWindow(previousForeground); }
    protected override void OnClosed(EventArgs e)
    {
        freshness.Stop();
        if (source is not null) { if (hotkeyRegistered) UnregisterHotKey(source.Handle, 1); source.RemoveHook(Hook); }
        base.OnClosed(e);
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string value, double size, Brush color, bool bold = false) => new()
    { Text = value, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    private static Button Button(string value, Action action)
    {
        var button = new Button { Content = value, Width = 44, Height = 44, FontSize = 22, Foreground = MutedBrush,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0) };
        button.Click += (_, _) => action(); return button;
    }
    private static void AddCard(Grid grid, int row, int column, string name, TextBlock value, TextBlock caption)
    {
        var content = new StackPanel { Margin = new Thickness(12) };
        content.Children.Add(Text(name, 11, MutedBrush, true));
        value.Margin = new Thickness(0, 8, 0, 2); content.Children.Add(value); content.Children.Add(caption);
        var card = new Border { Background = SurfaceBrush, CornerRadius = new CornerRadius(12), Margin = new Thickness(4), Height = 104, Child = content };
        Grid.SetRow(card, row); Grid.SetColumn(card, column); grid.Children.Add(card);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
