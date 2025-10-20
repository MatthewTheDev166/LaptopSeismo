using Windows.Devices.Sensors;

namespace LaptopSeismo;

/// <summary>
/// Wraps the Windows accelerometer API and exposes vibration magnitude readings.
/// </summary>
internal sealed class AccelerometerService : IDisposable
{
    private readonly Accelerometer? _accelerometer;
    private readonly object _syncLock = new();
    private bool _isRunning;

    public event EventHandler<AccelerometerSample>? ReadingAvailable;

    public bool IsAvailable => _accelerometer is not null;

    public AccelerometerService()
    {
        _accelerometer = Accelerometer.GetDefault();

        if (_accelerometer is not null)
        {
            uint desiredInterval = Math.Max(_accelerometer.MinimumReportInterval, 20);
            _accelerometer.ReportInterval = desiredInterval;
        }
    }

    public void Start()
    {
        if (_accelerometer is null)
        {
            return;
        }

        lock (_syncLock)
        {
            if (_isRunning)
            {
                return;
            }

            _accelerometer.ReadingChanged += OnReadingChanged;
            _isRunning = true;
        }
    }

    public void Stop()
    {
        if (_accelerometer is null)
        {
            return;
        }

        lock (_syncLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _accelerometer.ReadingChanged -= OnReadingChanged;
            _isRunning = false;
        }
    }

    private void OnReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
    {
        AccelerometerReading reading = args.Reading;

        double x = reading.AccelerationX;
        double y = reading.AccelerationY;
        double z = reading.AccelerationZ;

        double magnitude = Math.Sqrt((x * x) + (y * y) + (z * z));
        double delta = Math.Abs(magnitude - 1.0); // Remove gravity component.

        double magnitudeDelta = Math.Min(delta, 4.0); // Avoid excessive spikes.
        double axisX = Math.Clamp(x, -4.0, 4.0);
        double axisY = Math.Clamp(y, -4.0, 4.0);
        double axisZ = Math.Clamp(z - 1.0, -4.0, 4.0); // Remove gravity from Z.

        ReadingAvailable?.Invoke(this, new AccelerometerSample(axisX, axisY, axisZ, magnitudeDelta));
    }

    public void Dispose()
    {
        Stop();
    }
}

public readonly record struct AccelerometerSample(double AxisX, double AxisY, double AxisZ, double MagnitudeDelta);
