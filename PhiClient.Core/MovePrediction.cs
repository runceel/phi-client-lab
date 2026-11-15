using System.Collections.Immutable;

namespace PhiClient.Core;

public sealed record PendingMove(int X, int Y, long At);
public sealed record MovePrediction
{
    public ImmutableArray<PendingMove> Queue { get; init; } = [];
    public ImmutableArray<PendingMove> Guessed { get; init; } = [];
    public ImmutableArray<long> Samples { get; init; } = [];
    public MapPosition? Base { get; init; }
    public long? LastGuess { get; init; }
    public int Wait => (int)Math.Clamp(Samples.IsEmpty ? 30 : Samples.Max() + 30, 120, 1000);
    public MapPosition Offset => new(Guessed.Sum(m => (long)m.X), Guessed.Sum(m => (long)m.Y));

    public MovePrediction Sent(string text, long at)
    {
        if (text.Length != 4 || !text.StartsWith("go ", StringComparison.Ordinal)) return this;
        var step = char.ToLowerInvariant(text[3]) switch
        {
            'n' => new PendingMove(0, -1, at), 's' => new PendingMove(0, 1, at),
            'e' => new PendingMove(1, 0, at), 'w' => new PendingMove(-1, 0, at), _ => null
        };
        if (step is null) return this;
        if (Queue.Length + Guessed.Length >= 4096)
            throw new InvalidOperationException("移動予測の待ち行列が4096歩に達しました。サーバーからの応答を待ってください。");
        return this with { Queue = Queue.Add(step) };
    }

    public MovePrediction Tick(long now)
    {
        if (Queue.IsEmpty || now - Queue[0].At < Wait ||
            LastGuess is { } last && now - last < 100) return this;
        return this with { LastGuess = now, Guessed = Guessed.Add(Queue[0]), Queue = Queue.RemoveAt(0) };
    }

    private MovePrediction Measure(PendingMove move, long now)
    {
        var samples = Samples.Add(Math.Max(0, now - move.At));
        return this with { Samples = samples.Length <= 8 ? samples : samples.RemoveAt(0) };
    }

    public MovePrediction OnMap(MapPosition position, long now)
    {
        var next = this with { Base = position };
        if (Base == position) return next with { Guessed = [] };
        var all = Guessed.AddRange(Queue);
        long x = 0, y = 0;
        var matched = 0;
        if (Base is { } previous)
            for (var i = 0; i < all.Length; i++)
            {
                x += all[i].X;
                y += all[i].Y;
                if (position.X - previous.X == x && position.Y - previous.Y == y)
                {
                    matched = i + 1;
                    break;
                }
            }
        if (matched == 0) return next with { Guessed = [], Queue = Queue.IsEmpty ? Queue : Queue.RemoveAt(0) };
        var fromGuessed = Math.Min(matched, Guessed.Length);
        var last = all[matched - 1];
        if (matched > fromGuessed || now - last.At <= 400) next = next.Measure(last, now);
        return next with { Guessed = Guessed.RemoveRange(0, fromGuessed),
            Queue = Queue.RemoveRange(0, matched - fromGuessed) };
    }

    public MovePrediction CannotGo(long now) => !Guessed.IsEmpty ? this with { Guessed = Guessed.RemoveAt(0) } :
        Queue.IsEmpty ? this : Measure(Queue[0], now) with { Queue = Queue.RemoveAt(0) };
    public MovePrediction Reset() => new() { Samples = Samples };
}
