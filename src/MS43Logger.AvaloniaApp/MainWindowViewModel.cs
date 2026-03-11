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
    private AdxDefinition _currentDefinition;
    private CancellationTokenSource _pollingCts;
    private DispatcherTimer _uiTimer;
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly Stopwatch _sampleRateStopwatch = new();
    private long _sampleRateBaseCount;
    private StreamWriter _csvWriter;
    private string _searchText = string.Empty;
    private bool _enableCsvLogging;
    private string _csvPath;
    private int _visibleChannelCount;
    private double _samplesPerSecond;
    private bool _showPinnedOnly;
    private int _pinnedCount;

    private static readonly HashSet<string> DefaultPinnedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "RPM",
        "MAF",
        "TPS",
        "COOLANTTEMP",
        "BATTERYVOLTAGE",
        "FUELPULSEAVG",
    };

    public string[] AdxVariants { get; } = { "b22", "b25", "b30" };

    private string _selectedVariant = "b30";
    public string SelectedVariant
    {
        get => _selectedVariant;
        set => SetField(ref _selectedVariant, value);
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

    public string ConnectionButtonText => IsConnected ? "DISCONNECT" : "CONNECT";

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

    public MainWindowViewModel()
    {
        AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
        SelectedPort = AvailablePorts.FirstOrDefault();
        LiveValues = new ObservableCollection<DecodedValueViewModel>();
        CsvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MS43 Logs",
            $"ms43-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

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
            _currentDefinition = BundledDefinitions.Load(SelectedVariant);
            var channels = _currentDefinition.MatchChannels(Array.Empty<string>());

            if (EnableCsvLogging)
            {
                OpenCsvWriter(channels);
            }

            _session = Ds2Session.Create(_currentDefinition, SelectedPort);
            await Task.Run(() => _session.Connect());

            IsConnected = true;
            SampleCount = 0;
            SamplesPerSecond = 0;
            _sampleRateBaseCount = 0;
            _sampleRateStopwatch.Restart();
            StatusText = $"Connected to {SelectedPort}. Logging {channels.Count} channels from {SelectedVariant.ToUpperInvariant()}.";

            _allLiveValues.Clear();
            foreach (var channel in channels)
            {
                _allLiveValues.Add(new DecodedValueViewModel
                {
                    Id = channel.Id,
                    Title = channel.DisplayName.ToUpperInvariant(),
                    Units = SanitizeUnits(channel.Units),
                    Value = "--",
                    AccentBrush = GetAccentBrush(channel.Id, channel.DisplayName),
                    IsPinned = DefaultPinnedChannels.Contains(channel.Id)
                });
            }

            ApplyFilter();

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
            Disconnect();
        }
    }

    private async Task PollSensorsAsync(IReadOnlyList<AdxValueDefinition> channels, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var payload = _session.PollPayload();
                    var decoded = _currentDefinition.DecodePayload(payload, channels);
                    WriteCsvRow(decoded);

                    Dispatcher.UIThread.Post(() =>
                    {
                        SampleCount++;
                        foreach (var decodedValue in decoded)
                        {
                            var vm = _allLiveValues.FirstOrDefault(value => value.Id == decodedValue.Definition.Id);
                            if (vm is not null)
                            {
                                var digits = Math.Clamp(decodedValue.Definition.DigitCount, 0, 6);
                                vm.Value = decodedValue.Value.ToString($"F{digits}", CultureInfo.InvariantCulture);
                            }
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusText = $"Polling error: {ex.Message}";
                        Disconnect();
                    });
                    break;
                }

                await Task.Delay(20, token);
            }
        }, token);
    }

    public void Disconnect()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;

        _uiTimer?.Stop();
        _uiTimer = null;

        _sessionStopwatch.Stop();
        _sampleRateStopwatch.Stop();
        ElapsedTime = "00:00:00";
        SamplesPerSecond = 0;
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
        var elapsed = _sampleRateStopwatch.Elapsed.TotalSeconds;
        if (elapsed <= 0.1)
        {
            return;
        }

        SamplesPerSecond = Math.Max(0, (SampleCount - _sampleRateBaseCount) / elapsed);
        _sampleRateBaseCount = SampleCount;
        _sampleRateStopwatch.Restart();
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

    public string Id { get; set; }
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
