using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaptopSeismo;

public partial class MainWindow : Window
{
    private const double MaxDisplayAmplitude = 3.0; // g-force units shown in the graph.

    private readonly AccelerometerService _accelerometerService;
    private readonly Queue<double> _magnitudeSamples = new();
    private readonly Queue<double> _xSamples = new();
    private readonly Queue<double> _ySamples = new();
    private readonly Queue<double> _zSamples = new();

    private int _sampleCapacity = 256;
    private bool _isRunning;
    private bool _isLoaded;
    private bool _axisMode;

    public MainWindow()
    {
        InitializeComponent();

        _accelerometerService = new AccelerometerService();
        _accelerometerService.ReadingAvailable += OnAccelerometerReading;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        StopButton.IsEnabled = false;
        StartButton.IsEnabled = _accelerometerService.IsAvailable;
        AxisToggle.IsEnabled = _accelerometerService.IsAvailable;
        AxisToggle.IsChecked = false;
        _axisMode = false;

        StatusTextBlock.Text = _accelerometerService.IsAvailable
            ? "Accelerometer connected - ready to start."
            : "No sensor detected. Check device permissions or hardware.";

        Dispatcher.BeginInvoke(() =>
        {
            UpdateSampleCapacity();
            ResetSamples();
            UpdateBaseline();
        }, DispatcherPriority.Background);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _accelerometerService.Dispose();
    }

    private void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        if (!_accelerometerService.IsAvailable)
        {
            StatusTextBlock.Text = "Cannot start - no accelerometer available.";
            return;
        }

        ResetSamples();

        _accelerometerService.Start();
        _isRunning = true;

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusTextBlock.Text = "Streaming live accelerometer data...";
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isRunning)
        {
            return;
        }

        _accelerometerService.Stop();
        _isRunning = false;

        StartButton.IsEnabled = _accelerometerService.IsAvailable;
        StopButton.IsEnabled = false;
        StatusTextBlock.Text = "Paused. Click Start to resume capture.";
    }

    private void SensitivitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded)
        {
            return;
        }

        RedrawWaveform();
    }

    private void GraphCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        UpdateSampleCapacity();
        UpdateBaseline();
        RedrawWaveform();
    }

    private void UpdateSampleCapacity()
    {
        double width = GraphCanvas.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        int newCapacity = Math.Max(64, (int)Math.Round(width / 1.5));
        if (newCapacity == _sampleCapacity)
        {
            return;
        }

        _sampleCapacity = newCapacity;
        AdjustSampleBuffer();
    }

    private void AdjustSampleBuffer()
    {
        TrimQueue(_magnitudeSamples);
        TrimQueue(_xSamples);
        TrimQueue(_ySamples);
        TrimQueue(_zSamples);

        FillQueue(_magnitudeSamples);
        FillQueue(_xSamples);
        FillQueue(_ySamples);
        FillQueue(_zSamples);
    }

    private void TrimQueue(Queue<double> queue)
    {
        while (queue.Count > _sampleCapacity)
        {
            queue.Dequeue();
        }
    }

    private void FillQueue(Queue<double> queue)
    {
        while (queue.Count < _sampleCapacity)
        {
            queue.Enqueue(0);
        }
    }

    private void ResetSamples()
    {
        _magnitudeSamples.Clear();
        _xSamples.Clear();
        _ySamples.Clear();
        _zSamples.Clear();

        if (_sampleCapacity <= 0)
        {
            _sampleCapacity = 256;
        }

        for (int i = 0; i < _sampleCapacity; i++)
        {
            _magnitudeSamples.Enqueue(0);
            _xSamples.Enqueue(0);
            _ySamples.Enqueue(0);
            _zSamples.Enqueue(0);
        }

        UpdateMagnitudeDisplay(0);
        RedrawWaveform();
    }

    private void OnAccelerometerReading(object? sender, AccelerometerSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isRunning)
            {
                return;
            }

            if (_sampleCapacity > 0)
            {
                DequeueWhenFull(_magnitudeSamples);
                DequeueWhenFull(_xSamples);
                DequeueWhenFull(_ySamples);
                DequeueWhenFull(_zSamples);
            }

            _magnitudeSamples.Enqueue(Math.Min(sample.MagnitudeDelta, MaxDisplayAmplitude));
            _xSamples.Enqueue(Math.Clamp(sample.AxisX, -MaxDisplayAmplitude, MaxDisplayAmplitude));
            _ySamples.Enqueue(Math.Clamp(sample.AxisY, -MaxDisplayAmplitude, MaxDisplayAmplitude));
            _zSamples.Enqueue(Math.Clamp(sample.AxisZ, -MaxDisplayAmplitude, MaxDisplayAmplitude));

            UpdateMagnitudeDisplay(sample.MagnitudeDelta);
            RedrawWaveform();
        }, DispatcherPriority.Render);
    }

    private void DequeueWhenFull(Queue<double> queue)
    {
        if (queue.Count >= _sampleCapacity)
        {
            queue.Dequeue();
        }
    }

    private void RedrawWaveform()
    {
        double width = GraphCanvas.ActualWidth;
        double height = GraphCanvas.ActualHeight;

        if (width <= 0 || height <= 0 || _magnitudeSamples.Count == 0)
        {
            return;
        }

        double baselineY = height / 2;
        double amplitudePixels = Math.Max((height / 2) - 12, 8);
        double step = _sampleCapacity > 1 ? width / (_sampleCapacity - 1) : width;
        double sensitivity = SensitivitySlider.Value;

        if (_axisMode)
        {
            PopulateAxisPolyline(XAxisPolyline.Points, _xSamples, step, baselineY, amplitudePixels, sensitivity);
            PopulateAxisPolyline(YAxisPolyline.Points, _ySamples, step, baselineY, amplitudePixels, sensitivity);
            PopulateAxisPolyline(ZAxisPolyline.Points, _zSamples, step, baselineY, amplitudePixels, sensitivity);

            WaveformPolyline.Visibility = Visibility.Collapsed;
            XAxisPolyline.Visibility = Visibility.Visible;
            YAxisPolyline.Visibility = Visibility.Visible;
            ZAxisPolyline.Visibility = Visibility.Visible;
        }
        else
        {
            PopulateMagnitudePolyline(WaveformPolyline.Points, _magnitudeSamples, step, baselineY, amplitudePixels, sensitivity);

            WaveformPolyline.Visibility = Visibility.Visible;
            XAxisPolyline.Visibility = Visibility.Collapsed;
            YAxisPolyline.Visibility = Visibility.Collapsed;
            ZAxisPolyline.Visibility = Visibility.Collapsed;
        }
    }

    private static void PopulateMagnitudePolyline(PointCollection points, IEnumerable<double> samples, double step, double baselineY, double amplitudePixels, double sensitivity)
    {
        points.Clear();

        int index = 0;
        foreach (double raw in samples)
        {
            double scaled = Math.Min(raw * sensitivity, MaxDisplayAmplitude);
            double normalized = Math.Clamp(scaled / MaxDisplayAmplitude, 0, 1);
            double x = index * step;
            double y = baselineY - (normalized * amplitudePixels);

            points.Add(new Point(x, y));
            index++;
        }

        if (points.Count == 1)
        {
            points.Add(new Point(step, baselineY));
        }
    }

    private static void PopulateAxisPolyline(PointCollection points, IEnumerable<double> samples, double step, double baselineY, double amplitudePixels, double sensitivity)
    {
        points.Clear();

        int index = 0;
        foreach (double raw in samples)
        {
            double scaled = Math.Clamp(raw * sensitivity, -MaxDisplayAmplitude, MaxDisplayAmplitude);
            double normalized = scaled / MaxDisplayAmplitude; // -1 to 1
            double x = index * step;
            double y = baselineY - (normalized * amplitudePixels);

            points.Add(new Point(x, y));
            index++;
        }

        if (points.Count == 1)
        {
            points.Add(new Point(step, baselineY));
        }
    }

    private void UpdateBaseline()
    {
        double width = GraphCanvas.ActualWidth;
        double height = GraphCanvas.ActualHeight;
        double baseline = height / 2;

        BaselineLine.X1 = 0;
        BaselineLine.X2 = width;
        BaselineLine.Y1 = baseline;
        BaselineLine.Y2 = baseline;
    }

    private void UpdateMagnitudeDisplay(double magnitude)
    {
        MagnitudeTextBlock.Text = $"Magnitude: {magnitude:0.000} g";
    }

    private void AxisToggle_OnToggle(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _axisMode = AxisToggle.IsChecked == true;
        RedrawWaveform();
    }
}
