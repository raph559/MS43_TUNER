#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    private bool _isConnected;
    private string _statusText = "Disconnected";
    private int _sampleCount;
    private Ds2Session _session;
    private AdxDefinition _currentDefinition;
    private CancellationTokenSource _pollingCts;
    private DispatcherTimer _uiTimer;
    private readonly Stopwatch _sessionStopwatch = new();

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
            }
        }
    }

    public bool IsNotConnected => !IsConnected;

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

    public MainWindowViewModel()
    {
        AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
        SelectedPort = AvailablePorts.FirstOrDefault();
        LiveValues = new ObservableCollection<DecodedValueViewModel>();
    }

    public void RefreshPorts()
    {
        AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
        if (!AvailablePorts.Contains(SelectedPort))
        {
            SelectedPort = AvailablePorts.FirstOrDefault();
        }
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            StatusText = "Error: Please select a valid COM port.";
            return;
        }

        try
        {
            StatusText = $"Connecting to MS43 ({SelectedVariant}) on {SelectedPort}...";
            _currentDefinition = BundledDefinitions.Load(SelectedVariant);
            var channels = _currentDefinition.MatchChannels(Array.Empty<string>()); 

            _session = Ds2Session.Create(_currentDefinition, SelectedPort);
            
            await Task.Run(() => _session.Connect());

            IsConnected = true;
            StatusText = $"Connected! Logging {channels.Count} channels.";
            
            LiveValues.Clear();
            foreach (var ch in channels)
            {
                LiveValues.Add(new DecodedValueViewModel
                {
                    Id = ch.Id,
                    Title = ch.DisplayName.ToUpperInvariant(),
                    Units = ch.Units,
                    Value = "\u2014",
                    AccentBrush = GetAccentBrush(ch.Id, ch.DisplayName)
                });
            }

            _pollingCts = new CancellationTokenSource();
            _ = PollSensorsAsync(channels, _pollingCts.Token);
            _sessionStopwatch.Restart();
            _uiTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                (_, _) => ElapsedTime = _sessionStopwatch.Elapsed.ToString(@"hh\:mm\:ss"));
            _uiTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = $"Error connecting: {ex.Message}";
            Disconnect();
        }
    }

    private async Task PollSensorsAsync(System.Collections.Generic.IReadOnlyList<AdxValueDefinition> channels, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var payload = _session.PollPayload();
                    var decoded = _currentDefinition.DecodePayload(payload, channels);

                    Dispatcher.UIThread.Post(() =>
                    {
                        SampleCount++;
                        // Update UI
                        foreach (var decVal in decoded)
                        {
                            var vm = LiveValues.FirstOrDefault(v => v.Id == decVal.Definition.Id);
                            if (vm != null)
                            {
                                int digits = Math.Clamp(decVal.Definition.DigitCount, 0, 6);
                                vm.Value = decVal.Value.ToString($"F{digits}");
                            }
                        }
                    });
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

                await Task.Delay(20, token); // Roughly 50Hz polling max, depends on standard MS43 delay
            }
        });
    }

    public void Disconnect()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
        _uiTimer?.Stop();
        _uiTimer = null;
        _sessionStopwatch.Stop();
        ElapsedTime = "00:00:00";
        IsConnected = false;
        if (_session != null)
        {
            try { _session.Disconnect(); } catch { }
            _session.Dispose();
            _session = null;
        }
        StatusText = "Disconnected.";
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
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

    public string Id { get; set; }
    public string Title { get; set; }
    public string Units { get; set; }
    public IBrush AccentBrush { get; set; } = new SolidColorBrush(Color.Parse("#00e5ff"));

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
