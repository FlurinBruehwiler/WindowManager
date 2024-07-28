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
        var configFilePath = Path.Combine(AppContext.BaseDirectory, "WindowManager.config");

        const string defaultConfig = """
                                     //https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
                                     
                                     Alt+0x31=chrome.exe
                                     Alt+0x32=rider64.exe
                                     Alt+0x33=WindowsTerminal.exe
                                     """;

        string? config;

        if (File.Exists(configFilePath))
        {
            config = File.ReadAllText(configFilePath);
        }
        else
        {
            File.WriteAllText(configFilePath, defaultConfig);
            config = defaultConfig;
        }

        foreach (var configEntry in config.Split('\n')
                     .Where(x => !x.StartsWith("//") && !string.IsNullOrEmpty(x))
                     .Select(x => x.Trim().Split('='))
                     .Where(x => x.Length == 2)
                     .Select(x => ParseHotKey(x[0], x[1])))
        {
            if (configEntry != null)
            {
                HotKeyService.Shared.RegisterHotKey(configEntry);
            }
        }
    }

    private static HotKey? ParseHotKey(string keyCombo, string program)
    {
        var split = keyCombo.Split('+');

        if (split.Length < 2)
            return null;

        var keyString = split.Last();
        uint key;

        try
        {
            key = (uint)Convert.ToInt32(keyString, 16);
        }
        catch
        {
            return null;
        }

        var modifiers = split.Take(split.Length - 1).Select(x => x switch
        {
            "Alt" => HOT_KEY_MODIFIERS.MOD_ALT,
            "Ctrl" or "Control" => HOT_KEY_MODIFIERS.MOD_CONTROL,
            "Shift" => HOT_KEY_MODIFIERS.MOD_SHIFT,
            "Win" or "Windows" => HOT_KEY_MODIFIERS.MOD_WIN,
            _ => (HOT_KEY_MODIFIERS)0 //zero value will just be ignored in bitwise or
        })
        .Aggregate((HOT_KEY_MODIFIERS)0, (current, accumulator) => current | accumulator);

        return new HotKey(key, modifiers, () => FocusWindow(program));
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
