using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace test1
{
    public partial class HorizontalBattery : System.Windows.Controls.UserControl
    {
        // 中心文本自动轮播开关（百分比/温度）
        public static readonly DependencyProperty AutoRotateCenterProperty =
            DependencyProperty.Register(
                nameof(AutoRotateCenter), typeof(bool), typeof(HorizontalBattery),
                new PropertyMetadata(true, OnAutoRotateCenterChanged));

        // 轮播间隔（秒）
        public static readonly DependencyProperty RotateIntervalSecondsProperty =
            DependencyProperty.Register(
                nameof(RotateIntervalSeconds), typeof(int), typeof(HorizontalBattery),
                new PropertyMetadata(5, OnRotateIntervalSecondsChanged));

        private DispatcherTimer? _centerRotateTimer;
        // 可选：宽度变化是否平滑动画
        public static readonly DependencyProperty UseSmoothAnimationProperty =
            DependencyProperty.Register(
                nameof(UseSmoothAnimation), typeof(bool), typeof(HorizontalBattery),
                new PropertyMetadata(true));

        // 低电量临界阈值（0-1）
        public static readonly DependencyProperty CriticalThresholdProperty =
            DependencyProperty.Register(
                nameof(CriticalThreshold), typeof(double), typeof(HorizontalBattery),
                new PropertyMetadata(0.15));

        // 在低电量时是否闪烁
        public static readonly DependencyProperty IsBlinkOnCriticalProperty =
            DependencyProperty.Register(
                nameof(IsBlinkOnCritical), typeof(bool), typeof(HorizontalBattery),
                new PropertyMetadata(true));

        // 是否处于充电状态（用于显示高亮流动）
        public static readonly DependencyProperty IsChargingProperty =
            DependencyProperty.Register(
                nameof(IsCharging), typeof(bool), typeof(HorizontalBattery),
                new PropertyMetadata(false, OnChargingChanged));

        // 依赖属性：温度文本（例如 "37.8 °C"），在电池中央与百分比一起显示
        public static readonly DependencyProperty TemperatureTextProperty =
            DependencyProperty.Register(
                nameof(TemperatureText), typeof(string), typeof(HorizontalBattery),
                new PropertyMetadata("--", OnTemperatureTextChanged));

        // 是否在中心显示温度（true 显示温度；false 显示电量百分比）
        public static readonly DependencyProperty CenterShowsTemperatureProperty =
            DependencyProperty.Register(
                nameof(CenterShowsTemperature), typeof(bool), typeof(HorizontalBattery),
                new PropertyMetadata(false, OnCenterShowsTemperatureChanged));

        // 依赖属性：当前电量值
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(double), typeof(HorizontalBattery),
                new PropertyMetadata(0.0, OnValueOrMaximumChanged));

        // 依赖属性：最大电量
        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum), typeof(double), typeof(HorizontalBattery),
                new PropertyMetadata(100.0, OnValueOrMaximumChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public bool UseSmoothAnimation
        {
            get => (bool)GetValue(UseSmoothAnimationProperty);
            set => SetValue(UseSmoothAnimationProperty, value);
        }

        public double CriticalThreshold
        {
            get => (double)GetValue(CriticalThresholdProperty);
            set => SetValue(CriticalThresholdProperty, value);
        }

        public bool IsBlinkOnCritical
        {
            get => (bool)GetValue(IsBlinkOnCriticalProperty);
            set => SetValue(IsBlinkOnCriticalProperty, value);
        }

        public bool IsCharging
        {
            get => (bool)GetValue(IsChargingProperty);
            set => SetValue(IsChargingProperty, value);
        }

        public string TemperatureText
        {
            get => (string)GetValue(TemperatureTextProperty);
            set => SetValue(TemperatureTextProperty, value);
        }

        public bool CenterShowsTemperature
        {
            get => (bool)GetValue(CenterShowsTemperatureProperty);
            set => SetValue(CenterShowsTemperatureProperty, value);
        }

        public bool AutoRotateCenter
        {
            get => (bool)GetValue(AutoRotateCenterProperty);
            set => SetValue(AutoRotateCenterProperty, value);
        }

        public int RotateIntervalSeconds
        {
            get => (int)GetValue(RotateIntervalSecondsProperty);
            set => SetValue(RotateIntervalSecondsProperty, value);
        }

        public HorizontalBattery()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            UpdateBatteryDisplay();
            SetupCenterRotateTimer();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _centerRotateTimer?.Stop();
            _centerRotateTimer = null;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateBatteryDisplay();
        }

        private static void OnValueOrMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.UpdateBatteryDisplay();
        }

        private static void OnChargingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.UpdateChargingVisual((bool)e.NewValue);
        }

        private static void OnTemperatureTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.UpdateCenterText();
        }

        private static void OnCenterShowsTemperatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.UpdateCenterText();
        }

        private static void OnAutoRotateCenterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.SetupCenterRotateTimer();
        }

        private static void OnRotateIntervalSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var battery = (HorizontalBattery)d;
            battery.SetupCenterRotateTimer();
        }

        private void UpdateBatteryDisplay()
        {
            if (Maximum <= 0) return;

            double percentage = Math.Clamp(Value / Maximum, 0.0, 1.0);

            // 更新电量填充宽度（减去边距与边框）
            double maxFillWidth = Math.Max(0, BatteryOutline.ActualWidth - 8);
            double targetWidth = maxFillWidth * percentage;
            if (UseSmoothAnimation)
            {
                var anim = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ChargeLevelFill.BeginAnimation(FrameworkElement.WidthProperty, anim);
            }
            else
            {
                ChargeLevelFill.BeginAnimation(FrameworkElement.WidthProperty, null);
                ChargeLevelFill.Width = targetWidth;
            }

            // 根据电量百分比更新填充颜色：<10% 红，10%-20% 黄，>20% #FFB876DD
            if (percentage < 0.10)
                ChargeLevelFill.Fill = new SolidColorBrush(Colors.Red);
            else if (percentage < 0.20)
                ChargeLevelFill.Fill = new SolidColorBrush(Colors.Yellow);
            else
                ChargeLevelFill.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB876DD"));

            // 更新中央信息文本（百分比 + 温度）
            UpdateCenterText(percentage);

            // 低电量闪烁控制
            var blink = TryFindResource("BlinkStoryboard") as Storyboard;
            if (IsBlinkOnCritical && percentage <= CriticalThreshold)
            {
                blink?.Begin(this, true);
            }
            else
            {
                blink?.Stop(this);
                ChargeLevelFill.Opacity = 1.0;
            }

            // 充电效果可视性与动画控制（百分比100%时可隐藏）
            UpdateChargingVisual(IsCharging && percentage < 1.0);
        }

        private void UpdateCenterText(double? percentageOverride = null)
        {
            double percentage = percentageOverride ?? (Maximum > 0 ? Math.Clamp(Value / Maximum, 0.0, 1.0) : 0.0);
            if (PercentageText != null)
            {
                var percentText = string.Format("{0:P0}", percentage);
                var tempText = string.IsNullOrWhiteSpace(TemperatureText) ? "--" : TemperatureText;
                PercentageText.Text = CenterShowsTemperature ? tempText : percentText;
            }
        }

        private void UpdateChargingVisual(bool active)
        {
            var chargingSb = TryFindResource("ChargingStoryboard") as Storyboard;
            if (active)
            {
                ChargingOverlay.Visibility = Visibility.Visible;
                chargingSb?.Begin(this, true);
            }
            else
            {
                chargingSb?.Stop(this);
                ChargingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void SetupCenterRotateTimer()
        {
            _centerRotateTimer?.Stop();

            if (!AutoRotateCenter)
            {
                _centerRotateTimer = null;
                return;
            }

            int secs = Math.Max(1, RotateIntervalSeconds);
            _centerRotateTimer ??= new DispatcherTimer();
            _centerRotateTimer.Interval = TimeSpan.FromSeconds(secs);
            _centerRotateTimer.Tick -= OnCenterRotateTick;
            _centerRotateTimer.Tick += OnCenterRotateTick;
            _centerRotateTimer.Start();
        }

        private void OnCenterRotateTick(object? sender, EventArgs e)
        {
            CenterShowsTemperature = !CenterShowsTemperature;
        }
    }
}