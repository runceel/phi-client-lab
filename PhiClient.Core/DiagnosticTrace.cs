using System.Globalization;

namespace PhiClient.Core;

public enum DiagnosticEvent { Started, Stopped, Received, CommandSent, InputHandled, MapRefresh, MapWindowRefresh }

public sealed class DiagnosticTrace(TextWriter output, TimeProvider? clock = null) : IDisposable
{
    public const int MaximumEntries = 100000;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private int count;

    public void Write(DiagnosticEvent value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (count >= MaximumEntries) throw new IOException("時刻の記録が上限の100000件に達しました。");
        output.WriteLine($"{time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)} {value}");
        output.Flush();
        count++;
    }

    public void Dispose() => output.Dispose();
}
