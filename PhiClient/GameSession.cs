using PhiClient.Core;

namespace PhiClient;

public sealed class GameSession(SavedCharacter character)
{
    public SavedCharacter Character { get; } = character;
    public PhiConnection? Connection { get; set; }
    public bool Connecting { get; set; }
    public bool Connected { get; set; }
    public bool Closing { get; set; }
    public bool NorthUp { get; set; } = true;
    public string Status { get; set; } = "No status received.";
    public string Input { get; set; } = "";
    public string BoardText { get; set; } = "";
    public bool Editing { get; set; }
    public MapFrame? Map { get; set; }
    public IReadOnlyList<Choice> Choices { get; set; } = [];
    public Queue<string> Log { get; } = new();
    public int LogCharacters { get; private set; }

    public void Append(string text)
    {
        // Bound both row count and characters; a legal server line can still be 64 KiB.
        if (text.Length > 8192)
            text = text[..8192] + " [display truncated]";
        Log.Enqueue(text);
        LogCharacters += text.Length;
        while (Log.Count > 500 || LogCharacters > 100000)
            LogCharacters -= Log.Dequeue().Length;
    }
}
