using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class DiagnosticTraceTests
{
    [Fact]
    public void Trace_ContainsOnlyTypedEventsAndUtcMilliseconds()
    {
        using var text = new StringWriter();
        using (var trace = new DiagnosticTrace(text, new Clock()))
        {
            trace.Write(DiagnosticEvent.Started);
            trace.Write(DiagnosticEvent.Received);
            trace.Write(DiagnosticEvent.CommandSent);
            trace.Write(DiagnosticEvent.Stopped);
        }
        Assert.Equal(string.Join(Environment.NewLine, "1234 Started", "1234 Received", "1234 CommandSent",
            "1234 Stopped", ""), text.ToString());
    }

    [Fact]
    public void Trace_RejectsUnknownEventsAndSurfacesWriteFailure()
    {
        using var trace = new DiagnosticTrace(new BrokenWriter());
        Assert.Throws<ArgumentOutOfRangeException>(() => trace.Write((DiagnosticEvent)999));
        Assert.Throws<IOException>(() => trace.Write(DiagnosticEvent.MapRefresh));
    }

    [Fact]
    public void Trace_StopsAtTheBoundedEntryCount()
    {
        using var trace = new DiagnosticTrace(TextWriter.Null, new Clock());
        for (var index = 0; index < DiagnosticTrace.MaximumEntries; index++) trace.Write(DiagnosticEvent.MapRefresh);
        Assert.Throws<IOException>(() => trace.Write(DiagnosticEvent.MapRefresh));
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1234);
    }
    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("fixture write failure");
    }
}
