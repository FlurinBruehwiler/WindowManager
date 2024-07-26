using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowManager;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal static class Program
{
    public static void Main()
    {
        HotKeyService.Shared.RegisterHotKey(0x31, HOT_KEY_MODIFIERS.MOD_ALT, () => FocusWindow("chrome.exe"));
        HotKeyService.Shared.RegisterHotKey(0x32, HOT_KEY_MODIFIERS.MOD_ALT, () => FocusWindow("rider64.exe"));
        HotKeyService.Shared.RegisterHotKey(0x33, HOT_KEY_MODIFIERS.MOD_ALT, () => FocusWindow("WindowsTerminal.exe"));
    }

    private static void FocusWindow(string programName)
    {
        Console.WriteLine($"Hotkey was pressed, trying to open window for {programName}");

        var process = GetProcess(programName);
        if (process is not { } processId)
        {
            Console.WriteLine("Did not find process!");
            return;
        }

        Console.WriteLine($"Found process {processId}");

        var mainWindow = GetMainWindow(processId);

        if (mainWindow == HWND.Null)
        {
            Console.WriteLine("Did not find main window");
            return;
        }

        Console.WriteLine($"Found main window {mainWindow}");

        Show(mainWindow);

        Console.WriteLine("------------------");
    }

    private static unsafe void Show(HWND windowHandle)
    {
        var foregroundWindow = PInvoke.GetForegroundWindow();

        if (foregroundWindow == windowHandle)
        {
            Console.WriteLine("Window is already in foreground");
            return;
        }

        WINDOWPLACEMENT placement = default;
        placement.length = (uint)sizeof(WINDOWPLACEMENT);
        PInvoke.GetWindowPlacement(windowHandle, &placement);

        if (placement.showCmd is SHOW_WINDOW_CMD.SW_MINIMIZE or SHOW_WINDOW_CMD.SW_SHOWMINIMIZED)
        {
            PInvoke.ShowWindow(windowHandle, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        Console.WriteLine($"Setting foreground window {GetWindowName(windowHandle)}");
        PInvoke.SetForegroundWindow(windowHandle);
    }

    private static unsafe uint? GetProcess(ReadOnlySpan<char> name)
    {
        using SafeHandle snapshotHandle =
            PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);

        PROCESSENTRY32 process = new()
        {
            dwSize = (uint) sizeof(PROCESSENTRY32)
        };

        if (PInvoke.Process32First(snapshotHandle, ref process))
        {
            do
            {
                var spanName = process.szExeFile.AsReadOnlySpan();

                if (Equals(spanName, name))
                {
                    return process.th32ProcessID;
                }
            } while (PInvoke.Process32Next(snapshotHandle, ref process));
        }

        return null;
    }

    private static bool Equals(ReadOnlySpan<CHAR> str1, ReadOnlySpan<char> str2) //probably not correct for non ascii values
    {
        for (var i = 0; i < str2.Length; i++)
        {
            var c1 = str1[i];
            var c2 = str2[i];

            if (c1 != c2)
            {
                return false;
            }
        }

        return true;
    }

    struct WindowCallbackParameter
    {
        public long ProcessId;
        public HWND WindowHandle;
    }

    private static unsafe HWND GetMainWindow(long processId)
    {
        WindowCallbackParameter parameter = new()
        {
            ProcessId = processId
        };
        var ptr = &parameter;
        PInvoke.EnumWindows(WindowEnumCallback, new LPARAM((nint)ptr));

        return parameter.WindowHandle;
    }

    private static unsafe BOOL WindowEnumCallback(HWND windowHandle, LPARAM lParam)
    {
        var param = (WindowCallbackParameter*)lParam.Value;

        uint processId = 0;
        PInvoke.GetWindowThreadProcessId(windowHandle, &processId);

        if (param->ProcessId != processId)
            return true;

        if (!IsMainWindow(windowHandle))
            return true;

        param->WindowHandle = windowHandle;
        Console.WriteLine($"Found window: {GetWindowName(windowHandle)} (Main)");
        return false;

    }

    private static bool IsMainWindow(HWND windowHandle)
    {
        var style = PInvoke.GetWindowLong(windowHandle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        if ((style & 0x00000080L) != 0) //WS_EX_TOOLWINDOW
        {
            return false;
        }

        return PInvoke.IsWindowVisible(windowHandle) && PInvoke.GetWindow(windowHandle, GET_WINDOW_CMD.GW_OWNER) == HWND.Null;
    }

    private static unsafe string GetWindowName(HWND windowHandle)
    {
        const int length = 100;
        char* name = stackalloc char[length];
        PInvoke.GetWindowText(windowHandle, new PWSTR(name), length);
        var spanName = new ReadOnlySpan<char>(name, length);
        return spanName.ToString();
    }
}
