using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlackOut;

internal sealed class KeyboardBlocker : IDisposable
{
    private readonly HookProcedure procedure;
    private IntPtr hook;

    public KeyboardBlocker()
    {
        procedure = HandleKeyboard;
        hook = SetWindowsHookEx(13, procedure, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr HandleKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, message, data);

        // Consume both press and release events, including modifier keys.
        return (IntPtr)1;
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
