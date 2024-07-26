using System.Diagnostics.CodeAnalysis;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowManager;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class HotKeyService : IDisposable
{
    public static HotKeyService Shared = new();

    private readonly Dictionary<int, HotKey> _registerdHotkeys = new();
    private readonly Dictionary<int, HotKey> _hotKeysToRegister = new();
    private readonly List<int> _hotKeysToUnregister = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _cancellationToken;

    public HotKeyService()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        new Thread(ListenForHotKey).Start();
    }

    public int RegisterHotKey(uint key, HOT_KEY_MODIFIERS modifiers, Action callback)
    {
        var id = GetUniqueHotKeyId();
        _hotKeysToRegister.Add(id, new HotKey(key, modifiers, callback));
        return id;
    }

    public void UnregisterHotKey(int hotKeyId)
    {
        if (_hotKeysToRegister.Remove(hotKeyId))
            return;

        if (_registerdHotkeys.ContainsKey(hotKeyId))
            _hotKeysToUnregister.Add(hotKeyId);
    }

    private void ListenForHotKey()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            var status = PInvoke.PeekMessage(out var msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE);
            // var status = External.PeekMessageA(out var msg, IntPtr.Zero, 0, 0, External.PM_REMOVE);

            RegisterHotkeys();
            UnregisterHotKeys();

            Thread.Sleep(1);

            if (status == 0)
                continue;

            if (status == -1)
            {
                // _logger.LogInformation("Error while getting Hotkey message");
                continue;
            }

            if (msg.message != 0x312)
                continue;

            var hotKeyId = (int)msg.wParam.Value.ToUInt32();

            if (!_registerdHotkeys.TryGetValue(hotKeyId, out var hotkey))
                continue;

            // _logger.LogInformation("Received Hotkey: {hotkey}", hotkey);

            // ReSharper disable once MethodSupportsCancellation
            Task.Run(hotkey.Callback);
        }
        _hotKeysToUnregister.AddRange(_registerdHotkeys.Select(x => x.Key));
        UnregisterHotKeys();
        // _logger.LogInformation("Stop listening for Hotkeys");
    }

    private void UnregisterHotKeys()
    {
        foreach (var id in _hotKeysToUnregister)
        {
            if (!PInvoke.UnregisterHotKey(HWND.Null, id))
            {
                // _logger.LogError("Failed to unregister hot key with id {id}", id);
                continue;
            }

            _registerdHotkeys.Remove(id);
        }
    }

    private void RegisterHotkeys()
    {
        foreach (var (id, hotKey) in _hotKeysToRegister)
        {
            if (!PInvoke.RegisterHotKey(HWND.Null, id, hotKey.Modifiers, (uint)hotKey.Key))
            {
                // _logger.LogInformation("Error while registering hotkey: {hotKey}", hotKey);
                continue;
            }

            _registerdHotkeys.Add(id, hotKey);
            // _logger.LogInformation("Registered Hotkey: {hotKey}", hotKey);
        }

        _hotKeysToRegister.Clear();
    }

    private int GetUniqueHotKeyId()
    {
        var rand = new Random();
        int hotKeyId;

        do
        {
            hotKeyId = rand.Next();
        } while (_registerdHotkeys.ContainsKey(hotKeyId) || _hotKeysToRegister.ContainsKey(hotKeyId));

        return hotKeyId;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
    }
}

internal record HotKey(uint Key, HOT_KEY_MODIFIERS Modifiers, Action Callback);
