namespace PhiClient.Core;

public sealed record SelectionCommand(string Text, bool NextPage = false);

public sealed class ChoiceSelection
{
    private readonly List<Choice> items = [];
    public IReadOnlyList<Choice> Items => items;
    private readonly HashSet<string> checkedNumbers = [];
    public IReadOnlySet<string> Checked => checkedNumbers;
    public int Cursor { get; private set; }
    public int PageStart { get; private set; }
    public bool Waiting { get; private set; }
    public bool Active => items.Count > 0;

    public ChoiceSelection Copy()
    {
        var copy = new ChoiceSelection { Cursor = Cursor, PageStart = PageStart, Waiting = Waiting };
        copy.items.AddRange(items);
        copy.checkedNumbers.UnionWith(checkedNumbers);
        return copy;
    }

    public void Receive(IReadOnlyList<Choice> page)
    {
        if (page.Count == 0)
        {
            Clear();
            return;
        }
        if (Waiting)
        {
            checkedNumbers.Remove(items[^1].Number);
            items.RemoveAt(items.Count - 1);
            var overflow = Math.Max(0, items.Count + page.Count - 1000);
            items.RemoveRange(0, overflow);
            PageStart = items.Count;
        }
        else
        {
            Clear();
        }
        items.AddRange(page);
        checkedNumbers.RemoveWhere(number => !items.Any(item => item.Number == number));
        Cursor = PageStart;
        Waiting = false;
    }

    public void Clear()
    {
        items.Clear();
        checkedNumbers.Clear();
        PageStart = Cursor = 0;
        Waiting = false;
    }

    public void Move(int delta)
    {
        if (Active && !Waiting)
            Cursor = Math.Clamp(Cursor + delta, 0, items.Count - 1);
    }

    public void Toggle(int index)
    {
        if (Waiting || index < 0 || index >= items.Count)
            return;
        Cursor = index;
        if (!IsContinuation(index) && !checkedNumbers.Add(items[index].Number))
            checkedNumbers.Remove(items[index].Number);
    }

    public void ToggleCursor()
    {
        Toggle(Cursor);
        Move(1);
    }

    public SelectionCommand Pick(int index) =>
        index < 0 || index >= items.Count ? new(".") :
            new(items[index].Number, IsContinuation(index));

    public SelectionCommand Number(int ordinal) =>
        ordinal is < 1 or > 9 ? new(".") : Pick(PageStart + ordinal - 1);

    public SelectionCommand Confirm() => Checked.Count == 0 ? Pick(Cursor) :
        new(string.Join(' ', items.Where(item => Checked.Contains(item.Number)).Select(item => item.Number)));

    public void Begin(SelectionCommand command)
    {
        if (command.NextPage)
            Waiting = true;
        else
            Clear();
    }

    private bool IsContinuation(int index) => index == PageStart + 8 && index == items.Count - 1;
}
