using SharpLensAI.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

internal static class HotkeyTests
{
    public static void Run(Action<bool, string> check)
    {
        check(HotkeyGesture.Format(HotkeyGesture.DefaultModifiers, HotkeyGesture.DefaultKey) == "Ctrl + Shift + X", "Default shortcut label");
        check(HotkeyGesture.IsValid(ModifierKeys.Control | ModifierKeys.Alt, Key.S), "Valid hotkey accepted");
        check(!HotkeyGesture.IsValid(ModifierKeys.None, Key.S) && !HotkeyGesture.IsValid(ModifierKeys.Control, Key.LeftCtrl) && !HotkeyGesture.IsValid(ModifierKeys.Control, Key.F12), "Invalid hotkeys rejected");
        var firstWindow = new Window();
        var secondWindow = new Window();
        try
        {
            new WindowInteropHelper(firstWindow).EnsureHandle();
            new WindowInteropHelper(secondWindow).EnsureHandle();
            using var first = new GlobalHotkeyManager();
            using var second = new GlobalHotkeyManager();
            const ModifierKeys modifiers = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;
            var available = Enumerable.Range((int)Key.F1, 11).Select(k => (Key)k).Where(k => first.Register(firstWindow, modifiers, k)).Take(1).ToArray();
            check(available.Length == 1, "Windows native hotkey registration");
            Key key = available[0];
            check(first.Register(firstWindow, modifiers, key), "Unchanged hotkey registration is idempotent");
            check(!second.Register(secondWindow, modifiers, key), "Conflicting shortcut detected");
            first.Unregister();
            check(second.Register(secondWindow, modifiers, key), "Shortcut released while editing");
            second.Unregister();
            check(first.Register(firstWindow, modifiers, key), "Shortcut restored after editing");
            check(!first.Register(firstWindow, ModifierKeys.None, key) && first.IsRegistered, "Invalid change preserves active shortcut");
            Key other = Enumerable.Range((int)Key.A, 26).Select(k => (Key)k).First(k => second.Register(secondWindow, modifiers, k));
            check(!first.Register(firstWindow, modifiers, other) && first.IsRegistered && !second.Register(secondWindow, modifiers, key), "Conflict preserves previous registration");
            second.Unregister();
            check(first.Register(firstWindow, modifiers, other) && second.Register(secondWindow, modifiers, key), "Successful change releases previous shortcut");
            first.Dispose();
            check(second.Register(secondWindow, modifiers, other), "Disposal releases shortcut");
        }
        finally { firstWindow.Close(); secondWindow.Close(); }
    }
}
