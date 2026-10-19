#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private string _selectedPort;
    private ObservableCollection<string> _availablePorts;
    private ObservableCollection<DecodedValueViewModel> _liveValues;
    private readonly List<DecodedValueViewModel> _allLiveValues = new();
    private bool _isConnected;
    private string _statusText = "Disconnected";
    private int _sampleCount;
    private Ds2Session _session;
    private CancellationTokenSource _pollingCts;
    private DispatcherTimer _uiTimer;
    private readonly Stopwatch _sessionStopwatch = new();
    private LiveLogger _logger;
    private string _rateText = "No samples yet";
    private StreamWriter _csvWriter;
    private string _searchText = string.Empty;
    private bool _enableCsvLogging;
    private string _csvPath;
    private int _visibleChannelCount;
    private double _samplesPerSecond;
    private bool _showPinnedOnly;
    private int _pinnedCount;
    private string _selectedTheme = "Dark";

    private readonly Stopwatch _accelStopwatch = new();
    private string _time0To100 = "--";
    private string _time0To200 = "--";
    private bool _reached100;
    private bool _reached200;
    private bool _isPerformanceTestArmed;
    private bool _isWaitingForLaunch;

    public void ArmPerformanceTest()
    {
        _isPerformanceTestArmed = true;
        _isWaitingForLaunch = false;
        _accelStopwatch.Reset();
        _reached100 = false;
        _reached200 = false;
        Time0To100 = "Wait 0kmh";
        Time0To200 = "Wait 0kmh";
    }

    private static readonly HashSet<string> DefaultPinnedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "RPM",
        "MAF",
        "TPS",
        "COOLANTTEMP",
        "BATTERYVOLTAGE",
        "FUELPULSEAVG",
        "N",
        "MAFIGA",
        "IGAAVG",
        "TCO",
        "LAM1",
        "VB",
    };

    public string[] AdxVariants { get; } = { "b22", "b25", "b30", "ms43x" };

    public string[] ThemeModes { get; } = { "Dark", "Light" };

    private string _selectedVariant = "b30";
    public string SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (SetField(ref _selectedVariant, value))
            {
                PreviewChannels();
            }
        }
    }

    public ObservableCollection<string> AvailablePorts
    {
        get => _availablePorts;
        set => SetField(ref _availablePorts, value);
    }

    public string SelectedPort
    {
        get => _selectedPort;
        set => SetField(ref _selectedPort, value);
    }

    public ObservableCollection<DecodedValueViewModel> LiveValues
    {
        get => _liveValues;
        set => SetField(ref _liveValues, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(IsNotConnected));
                OnPropertyChanged(nameof(ConnectionButtonText));
            }
        }
    }

    public bool IsNotConnected => !IsConnected;

    public string ConnectionButtonText => IsConnected ? "Disconnect" : "Connect";

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public int SampleCount
    {
        get => _sampleCount;
        set => SetField(ref _sampleCount, value);
    }

    private string _elapsedTime = "00:00:00";
    public string ElapsedTime
    {
        get => _elapsedTime;
        set => SetField(ref _elapsedTime, value);
    }

    public string Time0To100
    {
        get => _time0To100;
        set => SetField(ref _time0To100, value);
    }

    public string Time0To200
    {
        get => _time0To200;
        set => SetField(ref _time0To200, value);
    }

    private void UpdateAccelerationTimes(double speed)
    {
        if (!_isPerformanceTestArmed)
        {
            return;
        }

        if (!_accelStopwatch.IsRunning)
        {
            if (speed <= 1.0 && !_isWaitingForLaunch)
            {
                _isWaitingForLaunch = true;
                Time0To100 = "Ready";
                Time0To200 = "Ready";
            }
            else if (_isWaitingForLaunch && speed > 1.0)
            {
                _accelStopwatch.Restart();
                Time0To100 = "00.0s";
                Time0To200 = "00.0s";
            }
        }
        else if (_accelStopwatch.IsRunning)
        {
            if (!_reached100)
            {
                Time0To100 = (_accelStopwatch.ElapsedMilliseconds / 1000.0).ToString("F1") + "s";
                if (speed >= 100.0)
                {
                    _reached100 = true;
                }
            }
            if (!_reached200)
            {
                Time0To200 = (_accelStopwatch.ElapsedMilliseconds / 1000.0).ToString("F1") + "s";
                if (speed >= 200.0)
                {
                    _reached200 = true;
                    _accelStopwatch.Stop();
                    _isPerformanceTestArmed = false; // Disable arm after completion
                }
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool EnableCsvLogging
    {
        get => _enableCsvLogging;
        set => SetField(ref _enableCsvLogging, value);
    }

    public string CsvPath
    {
        get => _csvPath;
        set => SetField(ref _csvPath, value);
    }

    public int VisibleChannelCount
    {
        get => _visibleChannelCount;
        set => SetField(ref _visibleChannelCount, value);
    }

    public double SamplesPerSecond
    {
        get => _samplesPerSecond;
        set => SetField(ref _samplesPerSecond, value);
    }

    private double _engineSpeedLevel;

    /// <summary>Engine speed as a fraction of 8 000 rpm, for the logo needle.</summary>
    public double EngineSpeedLevel
    {
        get => _engineSpeedLevel;
        set => SetField(ref _engineSpeedLevel, value);
    }

    public IReadOnlyList<GaugeViewModel> Gauges { get; } = GaugeViewModel.CreateCluster();

    /// <summary>Puts the rev counter's red zone at the open tune's limiter, or removes it.</summary>
    public void SetRedline(double? rpm) => Gauges[0].RedFrom = rpm;

    private void ResolveGauges(IReadOnlyList<AdxValueDefinition> channels)
    {
        foreach (var gauge in Gauges)
        {
            gauge.ChannelId = gauge.Candidates.FirstOrDefault(id => channels.Any(channel => channel.Id == id));
        }
    }

    public string RateText
    {
        get => _rateText;
        set => SetField(ref _rateText, value);
    }

    public bool ShowPinnedOnly
    {
        get => _showPinnedOnly;
        set
        {
            if (SetField(ref _showPinnedOnly, value))
            {
                ApplyFilter();
            }
        }
    }

    public int PinnedCount
    {
        get => _pinnedCount;
        set => SetField(ref _pinnedCount, value);
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetField(ref _selectedTheme, value))
            {
                App.SetTheme(value);
            }
        }
    }

    public MainWindowViewModel()
    {
        AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
        SelectedPort = AvailablePorts.FirstOrDefault();
        LiveValues = new ObservableCollection<DecodedValueViewModel>();
        App.SetTheme(SelectedTheme);
        CsvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MS43 Logs",
            $"ms43-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        PreviewChannels();
    }

    public event EventHandler<string> ConnectionFailed;

    public void RefreshPorts()
    {
        AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
        if (!AvailablePorts.Contains(SelectedPort))
        {
            SelectedPort = AvailablePorts.FirstOrDefault();
        }
    }

    public async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            Disconnect();
            return;
        }

        await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            StatusText = "Select a valid COM port before connecting.";
            return;
        }

        try
        {
            StatusText = $"Connecting to MS43 ({SelectedVariant}) on {SelectedPort}...";
            CurrentDefinition = BundledDefinitions.Load(SelectedVariant);
            var channels = CurrentDefinition.MatchChannels(Array.Empty<string>());

            if (EnableCsvLogging)
            {
                OpenCsvWriter(channels);
            }

            _session = Ds2Session.Create(CurrentDefinition, SelectedPort);
            await Task.Run(() => _session.Connect());

            IsConnected = true;
            SampleCount = 0;
            ResetRate();
            ResolveGauges(channels);
            StatusText = BuildConnectedStatus(channels.Count);

            BuildChannelRows(channels);

            _pollingCts = new CancellationTokenSource();
            _ = PollSensorsAsync(channels, _pollingCts.Token);
            _sessionStopwatch.Restart();
            _uiTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                ElapsedTime = _sessionStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                UpdateSampleRate();
            });
            _uiTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = $"Connect failed: {ex.Message}";
            ConnectionFailed?.Invoke(this, ex.ToString());
            Disconnect();
        }
    }

    /// <summary>Shows the selected definition's channels and dials before connecting.</summary>
    public void PreviewChannels()
    {
        if (IsConnected)
        {
            return;
        }

        try
        {
            var channels = BundledDefinitions.Load(SelectedVariant).MatchChannels(Array.Empty<string>());
            BuildChannelRows(channels);
            ResolveGauges(channels);
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot read the {SelectedVariant} definition: {ex.Message}";
        }
    }

    private void BuildChannelRows(IReadOnlyList<AdxValueDefinition> channels)
    {
        var pinned = _allLiveValues.Where(value => value.IsPinned).Select(value => value.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allLiveValues.Clear();
        foreach (var channel in channels)
        {
            _allLiveValues.Add(new DecodedValueViewModel
            {
                Id = channel.Id,
                Title = channel.DisplayName,
                Units = SanitizeUnits(channel.Units),
                Value = "–",
                AccentBrush = GetAccentBrush(channel.Id, channel.DisplayName),
                IsPinned = pinned.Contains(channel.Id) || DefaultPinnedChannels.Contains(channel.Id),
            });
        }

        ApplyFilter();
    }

    private Task PollSensorsAsync(IReadOnlyList<AdxValueDefinition> channels, CancellationToken token)
    {
        var logger = new LiveLogger();
        _logger = logger;
        logger.SampleReceived += (_, sample) =>
        {
            WriteCsvRow(sample.Values);
            Dispatcher.UIThread.Post(() => ShowSample(sample));
        };
        logger.Status += (_, status) => Dispatcher.UIThread.Post(() =>
            StatusText = status == "Resumed" ? BuildConnectedStatus(channels.Count) : status);
        logger.Failed += (_, error) => Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"Polling error: {error.Message}";
            ConnectionFailed?.Invoke(this, "Polling error: " + error);
            Disconnect();
        });

        return logger.RunAsync(_session, CurrentDefinition, channels, token);
    }

    /// <summary>The definition the logger decodes with; set on connect.</summary>
    public AdxDefinition CurrentDefinition { get; internal set; }

    /// <summary>Raised on the UI thread after each sample has been shown.</summary>
    public event EventHandler<LiveSample> SampleShown;

    internal void ShowSample(LiveSample sample)
    {
        // A poll still in flight when Disconnect ran is posted afterwards; it must not bring values or the cursor back.
        if (!IsConnected)
        {
            return;
        }

        ShowSample(sample.Values);
        SampleShown?.Invoke(this, sample);
    }

    internal void ShowSample(IReadOnlyList<DecodedValue> decoded)
    {
        SampleCount++;
        foreach (var decodedValue in decoded)
        {
            var id = decodedValue.Definition.Id;
            var vm = _allLiveValues.FirstOrDefault(value => value.Id == id);
            if (vm is not null)
            {
                var digits = Math.Clamp(decodedValue.Definition.DigitCount, 0, 6);
                vm.Value = decodedValue.Value.ToString($"F{digits}", CultureInfo.InvariantCulture);
                vm.Push(decodedValue.Value);
            }

            foreach (var gauge in Gauges.Where(gauge => gauge.ChannelId == id))
            {
                gauge.Value = decodedValue.Value;
            }

            if (id is "N" or "RPM")
            {
                EngineSpeedLevel = Math.Clamp(decodedValue.Value / 8000.0, 0, 1);
            }

            if (string.Equals(id, "VEHICLESPEED", StringComparison.OrdinalIgnoreCase))
            {
                UpdateAccelerationTimes(decodedValue.Value);
            }
        }
    }

    private string BuildConnectedStatus(int channelCount)
    {
        return $"Connected to {SelectedPort}. Logging {channelCount} channels from {SelectedVariant.ToUpperInvariant()}.";
    }

    public void Disconnect()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;

        _uiTimer?.Stop();
        _uiTimer = null;

        _sessionStopwatch.Stop();
        foreach (var gauge in Gauges)
        {
            gauge.Value = null;
        }

        ElapsedTime = "00:00:00";
        ResetRate();
        IsConnected = false;

        if (_session is not null)
        {
            try
            {
                _session.Disconnect();
            }
            catch
            {
            }

            _session.Dispose();
            _session = null;
        }

        _csvWriter?.Dispose();
        _csvWriter = null;
        StatusText = "Disconnected.";
    }

    public void TogglePinned(DecodedValueViewModel channel)
    {
        if (channel is null)
        {
            return;
        }

        channel.IsPinned = !channel.IsPinned;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        LiveValues.Clear();
        IEnumerable<DecodedValueViewModel> filtered = _allLiveValues;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(value =>
                value.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                value.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                value.Units.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (ShowPinnedOnly)
        {
            filtered = filtered.Where(value => value.IsPinned);
        }

        filtered = filtered
            .OrderByDescending(value => value.IsPinned)
            .ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var value in filtered)
        {
            LiveValues.Add(value);
        }

        PinnedCount = _allLiveValues.Count(value => value.IsPinned);
        VisibleChannelCount = LiveValues.Count;
    }

    private void OpenCsvWriter(IReadOnlyList<AdxValueDefinition> channels)
    {
        var path = CsvPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("CSV logging is enabled but no CSV path is set.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _csvWriter?.Dispose();
        _csvWriter = new StreamWriter(path, append: false);
        var header = new List<string> { "timestamp_utc", "sample_index" };
        header.AddRange(channels.Select(channel => channel.Id));
        _csvWriter.WriteLine(string.Join(",", header));
        _csvWriter.Flush();
    }

    private void WriteCsvRow(IReadOnlyList<DecodedValue> decoded)
    {
        if (_csvWriter is null)
        {
            return;
        }

        var row = new List<string>(decoded.Count + 2)
        {
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SampleCount.ToString(CultureInfo.InvariantCulture)
        };

        row.AddRange(decoded.Select(value =>
        {
            var digits = Math.Clamp(value.Definition.DigitCount, 0, 6);
            return value.Value.ToString($"F{digits}", CultureInfo.InvariantCulture);
        }));

        _csvWriter.WriteLine(string.Join(",", row));
        _csvWriter.Flush();
    }

    private void UpdateSampleRate()
    {
        var stats = _logger?.Rate;

        SamplesPerSecond = stats?.SamplesPerSecond ?? 0;
        RateText = stats is null
            ? "No samples yet"
            : string.Format(CultureInfo.InvariantCulture, "{0:0} samples/s, {1:0}–{2:0} ms", stats.SamplesPerSecond, stats.MinMs, stats.MaxMs);
    }

    private void ResetRate()
    {
        _logger = null;
        SamplesPerSecond = 0;
        RateText = "No samples yet";
    }

    private static string SanitizeUnits(string units)
    {
        if (string.IsNullOrWhiteSpace(units))
        {
            return string.Empty;
        }

        var start = units.LastIndexOf('(');
        var end = units.LastIndexOf(')');
        if (start >= 0 && end > start)
        {
            return units[(start + 1)..end];
        }

        return units;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static IBrush GetAccentBrush(string id, string name)
    {
        var key = (id + " " + name).ToLowerInvariant();
        if (key.Contains("rpm")) return new SolidColorBrush(Color.Parse("#ff6b35"));
        if (key.Contains("temp") || key.Contains("coolant") || key.Contains("therm")) return new SolidColorBrush(Color.Parse("#ff4e6a"));
        if (key.Contains("volt") || key.Contains("ubat") || key.Contains("battery")) return new SolidColorBrush(Color.Parse("#ffd60a"));
        if (key.Contains("throttle") || key.Contains("pedal") || key.Contains("tps")) return new SolidColorBrush(Color.Parse("#2eff9b"));
        if (key.Contains("speed") || key.Contains("velocity")) return new SolidColorBrush(Color.Parse("#c77dff"));
        return new SolidColorBrush(Color.Parse("#00e5ff"));
    }
}

public class DecodedValueViewModel : INotifyPropertyChanged
{
    private string _value;
    private bool _isPinned;

    private int _historyVersion;

    public string Id { get; set; }

    public SampleHistory History { get; } = new();

    /// <summary>Changes with every new sample so the sparkline redraws.</summary>
    public int HistoryVersion
    {
        get => _historyVersion;
        private set
        {
            _historyVersion = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HistoryVersion)));
        }
    }

    public void Push(double value)
    {
        History.Add(value);
        HistoryVersion++;
    }

    public string Title { get; set; }
    public string Units { get; set; } = string.Empty;
    public IBrush AccentBrush { get; set; } = new SolidColorBrush(Color.Parse("#00e5ff"));

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinGlyph)));
            }
        }
    }

    public string PinGlyph => IsPinned ? "★" : "☆";

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
