using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SharpLensAI.Services;

/// <summary>Регистрация глобальных комбинаций Windows, работающих и вне окна приложения.</summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int HotkeyId = 0x534C; // "SL"
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private Window? _window;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;
    public bool IsRegistered => _registered;

    public bool Register(Window window, ModifierKeys modifiers, Key key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);

        Unregister();
        _window = window;
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero)
            return false; // Вызывайте после SourceInitialized.

        if (_source is null)
        {
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
            window.Closed -= Window_Closed;
            window.Closed += Window_Closed;
        }

        if (_source is null) return false;

        uint nativeModifiers = ModNoRepeat;
        if (modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= ModWin;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(_handle, HotkeyId, nativeModifiers, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _handle != IntPtr.Zero)
            UnregisterHotKey(_handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        if (_window is not null)
            _window.Closed -= Window_Closed;
        _window = null;
        _handle = IntPtr.Zero;
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
