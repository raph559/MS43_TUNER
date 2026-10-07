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
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly TimeProvider _time;
    private readonly Func<string[]> _ports;
    private string _selectedPort;
    private ObservableCollection<string> _availablePorts;
    private ObservableCollection<DecodedValueViewModel> _liveValues;
    private readonly List<DecodedValueViewModel> _allLiveValues = new();
    private bool _isConnected;
    private string _statusText = "Disconnected";
    private int _sampleCount;
    private Ds2Session _session;
    private MS43Logger.Core.LiveTuning.WatchingPayloadSource _watching;
    private DiagnosticPayloadSource _diagnostics;              // the log's fault and state columns, around the logger's source
    private bool _overlayState;                               // the ECU runs overlay 10: its pops and blip state is logged
    private uint? _watchAsked;
    private CancellationTokenSource _pollingCts;
    private CancellationTokenSource _reconnectCts;
    private bool _isReconnecting;
    private DispatcherTimer _uiTimer;
    private readonly Stopwatch _sessionStopwatch = new();
    private LiveLogger _logger;
    private StreamWriter _csvWriter;
    private readonly object _csvLock = new();                 // the logger thread writes rows; the UI opens and closes the file
    private IReadOnlyList<AdxValueDefinition> _csvChannels;   // the session's channels, for a log started while connected
    private static readonly HashSet<string> StatusInputs = new(StringComparer.Ordinal)
    {
        nameof(IsConnected), nameof(IsConnecting), nameof(SelectedPort), nameof(SelectedVariant),
        nameof(EnableCsvLogging), nameof(CsvPath), nameof(ElapsedTime), nameof(SampleCount),
    };

    private bool _isConnecting;
    private bool _hasNoSamplesYet;
    private string _rateValue;
    private bool _isSlowLink;
    private string _rateDetail;
    private string _searchText = string.Empty;
    private bool _enableCsvLogging;
    private string _csvPath;
    private int _visibleChannelCount;
    private double _samplesPerSecond;
    private bool _showPinnedOnly;
    private int _pinnedCount;

    private ReadoutsMode _readouts = ReadoutsMode.Tiles;
    private int _trendSeconds = 10;
    private string _logFolder = AppSettings.DefaultLogFolder;
    private bool _startCsvOnConnect;
    private ConnectionMessage _banner;
    private string _bannerDetail;
    private string _connectionLog;
    private bool _isStale;
    private string _retryText;
    private DateTimeOffset _sessionStart;
    private DateTimeOffset? _lastSampleAt;
    private DispatcherTimer _staleTimer;
    private static readonly ConnectionMessage NoPort = new("No COM port found. Plug in the K-line cable, then rescan ports.", LiveAction.RescanPorts);
    private readonly RunClock _runClock = new();
    private AdxDefinition _currentDefinition;

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
        // Oil beside coolant (performance spec 9.3): TOIL in MS43X001, OILTEMP in the stock definitions.
        "TOIL",
        "OILTEMP",
    };

    public string[] AdxVariants { get; } = { "b22", "b25", "b30", "ms43x" };

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

    public string ConnectionButtonText => IsConnecting ? "Connecting…" : IsConnected ? "Disconnect" : "Connect";

    /// <summary>
    /// True while the session dropped without Disconnect (a key-off: the ECU stopped answering after it had answered) and a
    /// connect is tried every <see cref="ReconnectInterval"/>; the log stays open (live tuning spec 5.4).
    /// </summary>
    public bool IsReconnecting
    {
        get => _isReconnecting;
        private set
        {
            if (SetField(ref _isReconnecting, value))
            {
                OnPropertyChanged(nameof(ConnectionButtonText));
            }
        }
    }

    /// <summary>True from Connect until the ECU answers or the attempt fails.</summary>
    public bool IsConnecting
    {
        get => _isConnecting;
        internal set
        {
            if (SetField(ref _isConnecting, value))
            {
                OnPropertyChanged(nameof(ConnectionButtonText));
            }
        }
    }

    /// <summary>
    /// The status bar's connection line (system 5.11) in parts, so numbers can be set in Bahnschrift and the word
    /// "Connected" in NowText: "Connected to COM1 · ms43x · Logging 00:04:12 to ms43-20261019-2225.csv · 9576 rows".
    /// </summary>
    public IReadOnlyList<StatusPart> StatusParts
    {
        get
        {
            if (IsConnecting)
            {
                return [new StatusPart($"Connecting to MS43 ({SelectedVariant}) on {SelectedPort}…")];
            }

            if (!IsConnected)
            {
                return [new StatusPart("Not connected")];
            }

            if (_retryText is not null)
            {
                return [new StatusPart(_retryText)];
            }

            var parts = new List<StatusPart> { new("Connected", IsNow: true), new($" to {SelectedPort} · "), new(SelectedVariant, IsId: true) };
            if (EnableCsvLogging && !string.IsNullOrWhiteSpace(CsvPath))
            {
                parts.Add(new(" · Logging "));
                parts.Add(new(ElapsedTime, IsNumber: true));
                parts.Add(new($" to {Path.GetFileName(CsvPath)} · "));
                parts.Add(new(SampleCount.ToString(CultureInfo.InvariantCulture), IsNumber: true));
                parts.Add(new(" rows"));
            }
            else
            {
                parts.Add(new(" · Not writing a log"));
            }

            return parts;
        }
    }

    /// <summary>The connection line without the word "Connected".</summary>
    public string ConnectionDetail => string.Concat(StatusParts.Where(part => !part.IsNow).Select(part => part.Text));

    /// <summary>Connected, before the first sample arrived.</summary>
    public bool HasNoSamplesYet { get => _hasNoSamplesYet; private set => SetField(ref _hasNoSamplesYet, value); }

    /// <summary>The logging rate in whole samples per second, "38".</summary>
    public string RateValue { get => _rateValue; private set => SetField(ref _rateValue, value); }

    /// <summary>Under 10 samples/s, which the header marks as a slow link.</summary>
    public bool IsSlowLink { get => _isSlowLink; private set => SetField(ref _isSlowLink, value); }

    /// <summary>"Between samples: 24 to 31 ms", the rate's tooltip.</summary>
    public string RateDetail { get => _rateDetail; private set => SetField(ref _rateDetail, value); }

    internal void ShowRate(RateStats stats)
    {
        SamplesPerSecond = stats?.SamplesPerSecond ?? 0;
        HasNoSamplesYet = stats is null;
        RateValue = stats?.SamplesPerSecond.ToString("0", CultureInfo.InvariantCulture);
        IsSlowLink = stats is not null && stats.SamplesPerSecond < 10;
        RateDetail = stats is null
            ? null
            : string.Format(CultureInfo.InvariantCulture, "Between samples: {0:0} to {1:0} ms", stats.MinMs, stats.MaxMs);
    }

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

    private double _engineSpeedLevel;

    /// <summary>Engine speed as a fraction of 8 000 rpm, for the logo needle.</summary>
    public double EngineSpeedLevel
    {
        get => _engineSpeedLevel;
        set => SetField(ref _engineSpeedLevel, value);
    }

    public IReadOnlyList<GaugeViewModel> Gauges { get; } = GaugeViewModel.CreateCluster();

    /// <summary>The band's gear cell (system 8): GEAR, in both the MS43X001 and the stock definitions.</summary>
    public ReadoutViewModel GearReadout { get; } = new("Gear", string.Empty, 0, "GEAR");

    /// <summary>The band's speed: VS (MS43X001) or VEHICLESPEED (stock definitions), whole km/h.</summary>
    public ReadoutViewModel SpeedReadout { get; } = new("Vehicle Speed", "km/h", 0, "VS", "VEHICLESPEED");

    // Floored: GEAR's fraction is the high byte of N over 32, so rounding read one gear high above about 4100 rpm (performance spec 4.1).
    /// <summary>The engaged gear, 1 to 5; null in neutral, with the clutch in, or without a value (system 7.2: no slot lit).</summary>
    public int? Gear => GearReadout.Value is double gear && double.IsFinite(gear) && Math.Floor(gear) is >= 1 and <= 5 ? (int)Math.Floor(gear) : null;

    /// <summary>"4", or "n/a" when no gear is engaged or known.</summary>
    public string GearText => Gear?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    public double? VehicleSpeed => SpeedReadout.Value;

    /// <summary>The rpm readout's caption (system 7.2): the open bin's limiter, or how to get one.</summary>
    public string RevLimitCaption => Gauges[0].RedFrom is double limit
        ? $"Rev limit {NumberFormat.Format(limit, 0)} rpm, read from the open bin"
        : "Rev limit: open a bin to read it";

    public bool HasRevLimit => Gauges[0].RedFrom is not null;

    /// <summary>Tiles or dials in the band; everything else on the screen stays as it is (system 8).</summary>
    public ReadoutsMode Readouts
    {
        get => _readouts;
        set
        {
            if (SetField(ref _readouts, value))
            {
                OnPropertyChanged(nameof(ShowsTiles));
                OnPropertyChanged(nameof(ShowsDials));
            }
        }
    }

    public bool ShowsTiles => Readouts == ReadoutsMode.Tiles;

    public bool ShowsDials => Readouts == ReadoutsMode.Dials;

    /// <summary>The channel list's left column: the pinned channels, through the filter, in title order.</summary>
    public ObservableCollection<DecodedValueViewModel> PinnedValues { get; } = new();

    public bool HasPinned => TileValues.Count > 0;

    /// <summary>The channel list's right column: the channels not pinned, through the filter; empty with Pinned only.</summary>
    public ObservableCollection<DecodedValueViewModel> OtherValues { get; } = new();

    /// <summary>Every pinned channel, in title order, for the tiles under the band; the filter never hides a tile.</summary>
    public ObservableCollection<DecodedValueViewModel> TileValues { get; } = new();

    /// <summary>"No channel matches "xyz"." while the filter leaves both columns empty (system 7.2); null otherwise.</summary>
    public string NoMatchText => !string.IsNullOrWhiteSpace(SearchText) && PinnedValues.Count == 0 && OtherValues.Count == 0
        ? $"No channel matches \"{SearchText.Trim()}\"."
        : null;

    /// <summary>No channel is pinned: the Pinned column says how to pin one (system 5.6).</summary>
    public bool ShowsNoPins => TileValues.Count == 0 && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>The trend window in seconds (Settings: 10, 30 or 60). A change keeps the samples that still fit.</summary>
    public int TrendSeconds
    {
        get => _trendSeconds;
        set
        {
            if (value > 0 && SetField(ref _trendSeconds, value))
            {
                foreach (var row in _allLiveValues)
                {
                    row.Retrend(TimeSpan.FromSeconds(value));
                }

                OnPropertyChanged(nameof(TrendLabel));
            }
        }
    }

    /// <summary>The list's trend column, "Last 10 s": the history is a time window, whatever the rate.</summary>
    public string TrendLabel => $"Last {TrendSeconds.ToString(CultureInfo.InvariantCulture)} s";

    /// <summary>Where new, automatically named logs go (Settings). A change renames the next log while not logging.</summary>
    public string LogFolder
    {
        get => _logFolder;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetField(ref _logFolder, value) && !IsConnected)
            {
                CsvPath = NextCsvPath;
            }
        }
    }

    /// <summary>Settings' "Start the CSV log on connect": the Write CSV log switch starts from it.</summary>
    public bool StartCsvOnConnect
    {
        get => _startCsvOnConnect;
        set
        {
            if (SetField(ref _startCsvOnConnect, value) && !IsConnected)
            {
                EnableCsvLogging = value;
            }
        }
    }

    /// <summary>
    /// The next log's path, "{LogFolder}\ms43-20261019-2225xx.csv" from the clock; it never names a file that exists, so
    /// a second log in the same second becomes "-2", "-3" and so on.
    /// </summary>
    public string NextCsvPath
    {
        get
        {
            var name = "ms43-" + _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(LogFolder, name + ".csv");
            for (var n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(LogFolder, $"{name}-{n.ToString(CultureInfo.InvariantCulture)}.csv");
            }

            return path;
        }
    }

    /// <summary>Creates the log folder when it is missing and returns <see cref="NextCsvPath"/>.</summary>
    /// <summary>The Live banner's sentence (system 7.2), or null when there is nothing to report.</summary>
    public string Banner => _banner?.Text;

    public bool HasBanner => _banner is not null;

    /// <summary>The banner's second line: how old the frozen values are.</summary>
    public string BannerDetail { get => _bannerDetail; private set => SetField(ref _bannerDetail, value); }

    public LiveAction BannerAction => _banner?.Action ?? LiveAction.None;

    public string BannerActionText => BannerAction switch
    {
        LiveAction.Browse => "Browse…",
        LiveAction.RescanPorts => "Rescan ports",
        _ => null,
    };

    /// <summary>What went wrong, in full, for "Show details"; null until something failed.</summary>
    public string ConnectionLog { get => _connectionLog; private set => SetField(ref _connectionLog, value); }

    public bool HasConnectionLog => ConnectionLog is not null;

    /// <summary>The link stopped: the values shown are the last sample's, in Ink3 (system 7.2).</summary>
    public bool IsStale { get => _isStale; private set => SetField(ref _isStale, value); }

    /// <summary>"Show details" asks for the connection log; no window opens on its own when something fails.</summary>
    public event EventHandler ShowConnectionLogRequested;

    public void ShowConnectionLog() => ShowConnectionLogRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Connect failed: the banner names the cause and the fix.</summary>
    internal void ReportConnectFailure(Exception error)
    {
        StatusText = "Not connected.";
        ConnectionLog = error.ToString();
        BannerDetail = null;
        SetBanner(ConnectionMessages.NotConnected(error, SelectedPort, CsvPath));
    }

    /// <summary>The logger stopped: the banner says why and what was kept; values freeze in Ink3 if any arrived.</summary>
    internal void ReportLoggingStopped(Exception error)
    {
        var context = new StopContext(SelectedPort, EnableCsvLogging && !string.IsNullOrWhiteSpace(CsvPath) ? Path.GetFileName(CsvPath) : null,
            SampleCount, _time.GetUtcNow() - _sessionStart);
        var frozen = SampleCount > 0 && _lastSampleAt is not null;
        ConnectionLog = error.ToString();
        Disconnect(keepValues: frozen);
        SetBanner(ConnectionMessages.Stopped(error, context));
        IsStale = frozen;
        RefreshStaleLine();
        if (frozen && Avalonia.Application.Current is not null)
        {
            _staleTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshStaleLine());
            _staleTimer.Start();
        }
    }

    /// <summary>A session starts: the clock for its elapsed time, no banner, no frozen values.</summary>
    internal void BeginSession()
    {
        StopStaleTimer();
        _sessionStart = _time.GetUtcNow();
        _lastSampleAt = null;
        IsStale = false;
        BannerDetail = null;
        _retryText = null;
        SetBanner(null);
        SampleCount = 0;
        ResetWarmUp();
        IsConnected = true;
    }

    /// <summary>Renews the stale line from the clock: "…from the last sample, 9 s ago."</summary>
    internal void RefreshStaleLine()
    {
        BannerDetail = IsStale && _lastSampleAt is { } last
            ? ConnectionMessages.StaleLine((int)Math.Max(0, (_time.GetUtcNow() - last).TotalSeconds))
            : null;
    }

    /// <summary>One or two timeouts in a row: the status line says the ECU did not answer; values keep their last sample.</summary>
    internal void ShowRetrying(int timeouts)
    {
        _retryText = ConnectionMessages.Retrying(timeouts).Text;
        OnPropertyChanged(nameof(StatusParts));
        OnPropertyChanged(nameof(ConnectionDetail));
    }

    internal void ShowResumed()
    {
        _retryText = null;
        OnPropertyChanged(nameof(StatusParts));
        OnPropertyChanged(nameof(ConnectionDetail));
    }

    private void SetBanner(ConnectionMessage banner)
    {
        _banner = banner;
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(BannerAction));
        OnPropertyChanged(nameof(BannerActionText));
        OnPropertyChanged(nameof(HasConnectionLog));
    }

    private void StopStaleTimer()
    {
        _staleTimer?.Stop();
        _staleTimer = null;
    }

    private IEnumerable<ReadoutViewModel> Readings => Gauges.Cast<ReadoutViewModel>().Append(GearReadout).Append(SpeedReadout);

    /// <summary>Puts the rev counter's red zone at the open tune's limiter, or removes it.</summary>
    public void SetRedline(double? rpm)
    {
        Gauges[0].RedFrom = rpm;
        OnPropertyChanged(nameof(RevLimitCaption));
        OnPropertyChanged(nameof(HasRevLimit));
    }

    private void ResolveGauges(IReadOnlyList<AdxValueDefinition> channels)
    {
        foreach (var reading in Readings)
        {
            reading.Resolve(channels, SanitizeUnits);
        }
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

    public MainWindowViewModel(TimeProvider time = null, Func<string[]> ports = null)
    {
        _time = time ?? TimeProvider.System;
        _ports = ports ?? SerialPort.GetPortNames;
        GearReadout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReadoutViewModel.Value))
            {
                OnPropertyChanged(nameof(Gear));
                OnPropertyChanged(nameof(GearText));
            }
        };
        SpeedReadout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReadoutViewModel.Value))
            {
                OnPropertyChanged(nameof(VehicleSpeed));
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name && StatusInputs.Contains(name))
            {
                OnPropertyChanged(nameof(StatusParts));
                OnPropertyChanged(nameof(ConnectionDetail));
            }
        };
        WarmUp.Changed += (_, _) => ShowWarmUp();

        LiveValues = new ObservableCollection<DecodedValueViewModel>();
        RefreshPorts();
        CsvPath = NextCsvPath;
        PreviewChannels();
    }

    /// <summary>Lists the COM ports again; with none, the banner says to plug in the cable (system 7.2).</summary>
    public void RefreshPorts()
    {
        AvailablePorts = new ObservableCollection<string>(_ports());
        if (!AvailablePorts.Contains(SelectedPort))
        {
            SelectedPort = AvailablePorts.FirstOrDefault();
        }

        if (AvailablePorts.Count == 0 && !IsConnected)
        {
            SetBanner(NoPort);
        }
        else if (ReferenceEquals(_banner, NoPort))
        {
            SetBanner(null);
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
        IsConnecting = true;
        try
        {
            await ConnectCoreAsync();
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task ConnectCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            StatusText = "Select a valid COM port before connecting.";
            return;
        }

        try
        {
            StatusText = $"Connecting to MS43 ({SelectedVariant}) on {SelectedPort}…";
            CurrentDefinition = BundledDefinitions.Load(SelectedVariant);
            var channels = CurrentDefinition.MatchChannels(Array.Empty<string>());

            if (EnableCsvLogging)
            {
                OpenCsvWriter(channels);
            }

            _session = Ds2Session.Create(CurrentDefinition, SelectedPort);
            await Task.Run(() => _session.Connect());

            BeginSession();
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
            Disconnect();
            ReportConnectFailure(ex);
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
            PreviewChannels(BundledDefinitions.Load(SelectedVariant).MatchChannels(Array.Empty<string>()));
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot read the {SelectedVariant} definition: {ex.Message}";
        }
    }

    /// <summary>Lists these channels and resolves the band from them.</summary>
    internal void PreviewChannels(IReadOnlyList<AdxValueDefinition> channels)
    {
        BuildChannelRows(channels);
        ResolveGauges(channels);
    }

    private void BuildChannelRows(IReadOnlyList<AdxValueDefinition> channels)
    {
        var pinned = _allLiveValues.Where(value => value.IsPinned).Select(value => value.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allLiveValues.Clear();
        foreach (var channel in channels)
        {
            _allLiveValues.Add(new DecodedValueViewModel(TimeSpan.FromSeconds(TrendSeconds))
            {
                Id = channel.Id,
                Title = channel.DisplayName,
                Units = SanitizeUnits(channel.Units),
                Value = "n/a",
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
        {
            StatusText = status == "Resumed" ? BuildConnectedStatus(channels.Count) : status;
            if (status == "Resumed")
            {
                ShowResumed();
            }
        });
        logger.Retrying += (_, timeouts) => Dispatcher.UIThread.Post(() => ShowRetrying(timeouts));
        logger.Failed += (_, error) => Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"Polling error: {error.Message}";
            ReportLoggingStopped(error);
        });

        return logger.RunAsync(_session, CurrentDefinition, channels, token);
    }

    /// <summary>The definition the logger decodes with; set on connect.</summary>
    public AdxDefinition CurrentDefinition
    {
        get => _currentDefinition;
        internal set => SetField(ref _currentDefinition, value);
    }

    /// <summary>Raised on the UI thread after each sample has been shown.</summary>
    public event EventHandler<LiveSample> SampleShown;

    /// <summary>Coolant, oil and the overheat lamp as the samples report them (performance spec 5); reset on disconnect.</summary>
    public WarmUpMonitor WarmUp { get; } = new();

    /// <summary>The warm-up banners under the connection banner, danger first; Performance shows the same ones.</summary>
    public ObservableCollection<WarmUpBannerViewModel> WarmUpBanners { get; } = new();

    public bool HasWarmUpBanners => WarmUpBanners.Count > 0;

    /// <summary>
    /// The sample <see cref="SampleShown"/> is raised for, as the warm-up monitor read it, so the Performance tab decodes no
    /// sample a second time (minor review, finding 21); null without a definition.
    /// </summary>
    internal PerfFrame? LastFrame { get; private set; }

    /// <summary>The clock this screen reads; the Performance tab names its results by it.</summary>
    internal TimeProvider Time => _time;

    internal void ShowSample(LiveSample sample)
    {
        // A poll still in flight when Disconnect ran is posted afterwards; it must not bring values or the cursor back.
        if (!IsConnected)
        {
            return;
        }

        ShowSample(sample.Values, sample.Time);

        // Before SampleShown: a performance run reads the warm-up state this sample left (plan Task 6 ruling), from this frame.
        LastFrame = CurrentDefinition is { } definition
            ? PerfChannels.Read(definition, sample.Values, sample.Payload.Span, _runClock.At(sample), sample.Retries)
            : null;
        if (LastFrame is { } frame)
        {
            WarmUp.Push(frame);
        }

        SampleShown?.Invoke(this, sample);
    }

    private void ShowWarmUp()
    {
        var banners = WarmUp.Banners;
        // The same banners with a new reading change in place; a new or cleared banner rebuilds the list (and slides in).
        if (banners.Select(banner => (banner.Code, banner.Level)).SequenceEqual(WarmUpBanners.Select(shown => (shown.Code, shown.Banner.Level))))
        {
            for (var i = 0; i < banners.Count; i++)
            {
                WarmUpBanners[i].Update(banners[i]);
            }

            return;
        }

        WarmUpBanners.Clear();
        foreach (var banner in banners)
        {
            WarmUpBanners.Add(new WarmUpBannerViewModel(banner));
        }

        OnPropertyChanged(nameof(HasWarmUpBanners));
    }

    // A new link starts from first readings: warm at the first reading needs no wait, cold needs its 10 s (spec 5).
    private void ResetWarmUp()
    {
        _runClock.Reset();
        WarmUp.Reset();
    }

    /// <summary>Shows one sample; <paramref name="time"/> is when it arrived (the clock's now when not given).</summary>
    internal void ShowSample(IReadOnlyList<DecodedValue> decoded, DateTimeOffset? time = null)
    {
        var at = time ?? _time.GetUtcNow();
        _lastSampleAt = _time.GetUtcNow();
        SampleCount++;

        // The band shows what the last sample held: a channel missing from it reads n/a (system 7.2).
        var inSample = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var decodedValue in decoded)
        {
            inSample[decodedValue.Definition.Id] = decodedValue.Value;
        }

        foreach (var reading in Readings)
        {
            reading.Value = reading.ChannelId is { } channel && inSample.TryGetValue(channel, out var reached) ? reached : null;
        }

        // So do the rows: a channel the sample did not hold reads n/a until it comes back; its trend stays.
        foreach (var row in _allLiveValues.Where(row => !inSample.ContainsKey(row.Id)))
        {
            row.Value = "n/a";
        }

        foreach (var decodedValue in decoded)
        {
            var id = decodedValue.Definition.Id;
            var vm = _allLiveValues.FirstOrDefault(value => value.Id == id);
            if (vm is not null)
            {
                var digits = Math.Clamp(decodedValue.Definition.DigitCount, 0, 6);
                vm.Value = NumberFormat.Format(decodedValue.Value, digits);
                vm.Push(at, decodedValue.Value);
            }

            if (id is "N" or "RPM")
            {
                EngineSpeedLevel = Math.Clamp(decodedValue.Value / 8000.0, 0, 1);
            }
        }
    }

    private string BuildConnectedStatus(int channelCount)
    {
        return $"Connected to {SelectedPort}. Logging {channelCount} channels from {SelectedVariant.ToUpperInvariant()}.";
    }

    /// <summary>Disconnects; every value goes back to n/a and the next log gets a new name.</summary>
    public void Disconnect() => Disconnect(keepValues: false);

    // After a failure the values stay, frozen, under the banner that explains them.
    private void Disconnect(bool keepValues)
    {
        _pollingCts?.Cancel();
        _pollingCts = null;

        _uiTimer?.Stop();
        _uiTimer = null;

        _sessionStopwatch.Stop();
        _retryText = null;
        StopStaleTimer();
        IsStale = false;
        if (!keepValues)
        {
            foreach (var reading in Readings)
            {
                reading.Value = null;
            }

            foreach (var row in _allLiveValues)
            {
                row.Clear();
            }
        }

        ElapsedTime = "00:00:00";
        ResetRate();
        IsConnected = false;

        // After IsConnected: a performance run ends on the link loss with the warm-up state it ran in (review focus 1).
        ResetWarmUp();

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

        // Connecting again never overwrites the log just closed.
        CsvPath = NextCsvPath;
    }

    /// <summary>Live's undo history: channel pins. Tune's edits have their own, so Ctrl Z here never changes the bin.</summary>
    public UndoStack Undos { get; } = new();

    public void TogglePinned(DecodedValueViewModel channel)
    {
        if (channel is null)
        {
            return;
        }

        var label = (channel.IsPinned ? "Unpin " : "Pin ") + channel.Title;
        FlipPinned(channel);
        Undos.Push(new UndoStep(label, () => FlipPinned(channel), () => FlipPinned(channel)));
    }

    private void FlipPinned(DecodedValueViewModel channel)
    {
        channel.IsPinned = !channel.IsPinned;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        LiveValues.Clear();
        PinnedValues.Clear();
        OtherValues.Clear();
        IEnumerable<DecodedValueViewModel> filtered = _allLiveValues;
        // Every word somewhere in the channel's ID, name or units, as the Tune rail searches its items.
        var words = MS43Logger.Core.Tuning.XdfCatalog.Words(SearchText);
        if (words.Length > 0)
        {
            filtered = filtered.Where(value => words.All(word =>
                value.Id.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                value.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                value.Units.Contains(word, StringComparison.OrdinalIgnoreCase)));
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
            (value.IsPinned ? PinnedValues : OtherValues).Add(value);
        }

        PinnedCount = _allLiveValues.Count(value => value.IsPinned);
        VisibleChannelCount = LiveValues.Count;

        // The tiles follow the pins only, never the filter.
        var pins = _allLiveValues.Where(value => value.IsPinned).OrderBy(value => value.Title, StringComparer.OrdinalIgnoreCase).ToList();
        if (!pins.SequenceEqual(TileValues))
        {
            TileValues.Clear();
            foreach (var pin in pins)
            {
                TileValues.Add(pin);
            }

            OnPropertyChanged(nameof(HasPinned));
        }

        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(ShowsNoPins));
    }

    private void OpenCsvWriter(IReadOnlyList<AdxValueDefinition> channels)
    {
        var path = CsvPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("CSV logging is enabled but no CSV path is set.");
        }

        try
        {
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CsvWriteException(path, error);
        }
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

        try
        {
            _csvWriter.WriteLine(string.Join(",", row));
            _csvWriter.Flush();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The logger stops on it; the banner says which file failed and that the rows before are kept.
            throw new CsvWriteException(CsvPath, error);
        }
    }

    private void UpdateSampleRate()
    {
        ShowRate(_logger?.Rate);
    }

    private void ResetRate()
    {
        _logger = null;
        ShowRate(null);
    }

    private static string SanitizeUnits(string units)
    {
        if (string.IsNullOrWhiteSpace(units))
        {
            return string.Empty;
        }

        // The ADX writes "Engine Speed (rpm)"; an entry without the brackets holds a name ("Gear"), not a unit.
        var start = units.LastIndexOf('(');
        var end = units.LastIndexOf(')');
        return start >= 0 && end > start ? units[(start + 1)..end] : string.Empty;
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

}

/// <summary>One channel of the Live list (system 5.6): its title, id, unit, the last value and its trend.</summary>
public class DecodedValueViewModel : INotifyPropertyChanged
{
    private string _value;
    private bool _isPinned;
    private int _historyVersion;
    private SampleHistory _history;

    public DecodedValueViewModel(TimeSpan trend)
    {
        _history = new SampleHistory(trend);
    }

    public string Id { get; set; }

    public string Title { get; set; }

    /// <summary>"Long Term Fuel Trim Multiplicative Bank 1 TIADFAC1": the row's tooltip, since the list trims long names.</summary>
    public string FullName => $"{Title} {Id}";

    public string Units { get; set; } = string.Empty;

    /// <summary>The samples of the trend window, by time.</summary>
    public SampleHistory History
    {
        get => _history;
        private set
        {
            _history = value;
            Raise(nameof(History));
        }
    }

    /// <summary>Changes with every new sample so the sparkline redraws.</summary>
    public int HistoryVersion
    {
        get => _historyVersion;
        private set
        {
            _historyVersion = value;
            Raise(nameof(HistoryVersion));
        }
    }

    public void Push(DateTimeOffset time, double value)
    {
        History.Add(time, value);
        HistoryVersion++;
    }

    /// <summary>A new trend length: a new window that keeps the samples that still fit.</summary>
    public void Retrend(TimeSpan trend)
    {
        var history = new SampleHistory(trend);
        foreach (var (time, value) in History.Points)
        {
            history.Add(time, value);
        }

        History = history;
        HistoryVersion++;
    }

    /// <summary>Not connected: no value and no trend.</summary>
    public void Clear()
    {
        Value = "n/a";
        History.Clear();
        HistoryVersion++;
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                Raise(nameof(IsPinned));
                Raise(nameof(PinTip));
                Raise(nameof(PinName));
            }
        }
    }

    /// <summary>The pin button's tooltip and accessible name (system 5.6).</summary>
    public string PinTip => IsPinned ? "Unpin" : "Pin to top";

    public string PinName => $"{(IsPinned ? "Unpin" : "Pin")} {Title}";

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                Raise(nameof(Value));
                Raise(nameof(HasValue));
            }
        }
    }

    /// <summary>False while the last sample did not hold the channel and the value reads "n/a".</summary>
    public bool HasValue => !string.Equals(_value, "n/a", StringComparison.Ordinal);

    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One run of the status bar's connection line: numbers in Bahnschrift, ids in Cascadia, "Connected" in NowText.</summary>
public sealed record StatusPart(string Text, bool IsNumber = false, bool IsNow = false, bool IsId = false);
