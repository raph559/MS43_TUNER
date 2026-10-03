namespace MS43Logger.AvaloniaApp;

public sealed class LimiterRpmRow(int index) : ViewModelBase
{
    private string _rpm = string.Empty;
    public string Label => index == 0 ? "Neutral / gear 0" : $"Gear {index}";
    public string Rpm { get => _rpm; set => SetField(ref _rpm, value); }
}
