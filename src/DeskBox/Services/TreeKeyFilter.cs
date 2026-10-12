namespace DeskBox.Services;

/// <summary>Counts physical typing presses, never characters or key sequences.</summary>
public sealed class TreeKeyFilter
{
    private readonly HashSet<int> _down = [];

    public bool Process(int key, bool down, bool injected)
    {
        if (injected) return false;
        if (!down) { _down.Remove(key); return false; }
        if (!_down.Add(key)) return false; // OS auto-repeat
        if (_down.Overlaps([0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5, 0x5B, 0x5C])) return false;
        return key is >= 0x30 and <= 0x39 // number row
            or >= 0x41 and <= 0x5A // letters
            or >= 0x60 and <= 0x6F // numpad
            or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 // punctuation
            or 0x20 or 0x0D or 0x08; // space, enter, backspace
    }

    public void Reset() => _down.Clear();
}
