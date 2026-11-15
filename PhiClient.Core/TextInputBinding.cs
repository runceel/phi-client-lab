namespace PhiClient.Core;

public sealed class TextInputBinding(Action<string> onChanged)
{
    private Action<string> changed = onChanged;
    private bool applying;

    public void Apply(string value, Action<string> onChanged, Action<string> write)
    {
        changed = onChanged;
        var previous = applying;
        applying = true;
        try { write(value); }
        finally { applying = previous; }
    }

    public void Edited(string value)
    {
        if (!applying) changed(value);
    }
}
