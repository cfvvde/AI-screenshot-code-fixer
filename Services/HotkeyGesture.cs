using System.Windows.Input;
namespace SharpLensAI.Services;
public static class HotkeyGesture
{
    public const ModifierKeys DefaultModifiers = ModifierKeys.Control | ModifierKeys.Shift;
    public const Key DefaultKey = Key.X;
    public static bool IsValid(ModifierKeys modifiers, Key key) =>
        modifiers != ModifierKeys.None && (modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 &&
        (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F11 or >= Key.NumPad0 and <= Key.NumPad9);
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join(" + ", parts);
    }
}
