namespace PhiClient.Core;

public sealed record LogScrollAnchor(long Row, int Character, double Top);

public sealed class LogScrollState
{
    public bool Following { get; private set; } = true;
    public bool Enabled { get; private set; } = true;
    public bool Searching { get; set; }
    public LogScrollAnchor? Anchor { get; set; }
    public double Offset { get; set; }

    public static bool AtLatest(double offset, double maximum) => maximum - offset <= 1;

    public void Configure(bool enabled)
    {
        if (enabled && !Enabled)
        {
            if (Searching) Following = true;
            else Latest();
        }
        Enabled = enabled;
    }

    public void UserScrolled(double offset, double maximum)
    {
        Offset = offset;
        Following = AtLatest(offset, maximum);
    }

    public void Latest()
    {
        Following = true;
        Anchor = null;
        Offset = double.PositiveInfinity;
    }

    public double Target(double maximum, Func<LogScrollAnchor, double?> resolve) =>
        Math.Clamp(Enabled && Following && !Searching ? maximum : Anchor is { } anchor
            ? resolve(anchor) ?? Offset : Offset, 0, Math.Max(0, maximum));
}
