namespace PhiClient.Core;

public readonly record struct StatusBarValue(long? Current, long? Maximum)
{
    public double Percentage => Current is null || Maximum is null or <= 0
        ? 0 : Math.Clamp((double)Current.Value / Maximum.Value, 0, 1) * 100;

    public string Text => $"{Current?.ToString() ?? "--"} / {Maximum?.ToString() ?? "--"}";
}
