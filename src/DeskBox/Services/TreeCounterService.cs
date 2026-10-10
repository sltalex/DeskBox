using System.Runtime.InteropServices;
using DeskBox.Core.Persistence;
using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>Single process-wide tree counter. The hook only observes key codes and never blocks input.</summary>
public sealed class TreeCounterService : IDisposable
{
    private static readonly Lazy<TreeCounterService> s_instance = new(() => new TreeCounterService());
    public static TreeCounterService Instance => s_instance.Value;
    public static void FlushIfStarted()
    {
        if (s_instance.IsValueCreated) s_instance.Value.Flush();
    }
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly TreeKeyFilter _filter = new();
    private readonly Win32Helper.LowLevelKeyboardProc _callback;
    private readonly string _path = Path.Combine(DeskBoxDataPathService.Current.DataDirectory, "tree", "progress.json");
    private readonly TreeProgressFileStore _store;
    private readonly Timer _saveTimer;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private bool _enabled;
    private bool _disposed;
    private long _callbackTicks;
    private long _probeTicks;
    private TreeProgress _progress;

    private TreeCounterService()
    {
        _callback = HookCallback;
        _store = new TreeProgressFileStore(_path);
        _progress = _store.Load();
        _saveTimer = new Timer(_ => { try { Save(); CheckHook(); } catch (Exception ex) { App.Log($"[Tree] Save/check failed: {ex.Message}"); } }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public event Action? Changed;
    public TreeProgress Snapshot
    {
        get { lock (_gate) { _progress.RollDay(DateTime.Now); return Clone(_progress); } }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            _filter.Reset();
        }
        if (enabled && !Snapshot.Paused) StartHook(); else { StopHook(); Save(); }
        Changed?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        lock (_gate) { _progress.Paused = paused; _filter.Reset(); }
        if (paused) StopHook(); else if (_enabled) StartHook();
        Save(); Changed?.Invoke();
    }

    public void SetGoal(int goal)
    {
        lock (_gate) _progress.SetGoal(goal);
        Save(); Changed?.Invoke();
    }

    public long Harvest()
    {
        long result;
        lock (_gate) result = _progress.Harvest();
        Save(); Changed?.Invoke();
        return result;
    }

    public void Reset()
    {
        lock (_gate) _progress = new TreeProgress { DailyGoal = _progress.DailyGoal, Paused = _progress.Paused };
        Save(); Changed?.Invoke();
    }

    private void StartHook()
    {
        lock (_gate)
        {
            if (_disposed || _thread?.IsAlive == true) return;
            _thread = new Thread(HookThread) { IsBackground = true, Name = "DeskBox.TreeKeyCounter" };
            _thread.Start();
        }
    }

    private void StopHook()
    {
        Thread? thread;
        uint id;
        lock (_gate) { thread = _thread; id = _threadId; }
        if (id != 0) Win32Helper.PostThreadMessage(id, Win32Helper.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        if (thread != Thread.CurrentThread) thread?.Join(500);
        lock (_gate) _filter.Reset();
    }

    private void HookThread()
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            uint id = Win32Helper.GetCurrentThreadId();
            Win32Helper.PeekMessage(out _, IntPtr.Zero, 0, 0, Win32Helper.PM_NOREMOVE);
            hook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_KEYBOARD_LL, _callback, Win32Helper.GetModuleHandle(null), 0);
            lock (_gate) { _threadId = id; _hook = hook; }
            if (hook == IntPtr.Zero) { App.Log($"[Tree] Keyboard hook failed: {Marshal.GetLastWin32Error()}"); return; }
            lock (_gate) { if (!_enabled || _progress.Paused || _disposed) return; }
            while (Win32Helper.GetMessage(out Win32Helper.MSG message, IntPtr.Zero, 0, 0) > 0)
            { Win32Helper.TranslateMessage(in message); Win32Helper.DispatchMessage(in message); }
        }
        catch (Exception ex) { App.Log($"[Tree] Keyboard hook stopped: {ex.Message}"); }
        finally
        {
            if (hook != IntPtr.Zero) Win32Helper.UnhookWindowsHookEx(hook);
            lock (_gate) { _hook = IntPtr.Zero; _threadId = 0; _thread = null; _filter.Reset(); }
        }
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr dataPointer)
    {
        Volatile.Write(ref _callbackTicks, Environment.TickCount64);
        IntPtr hook = _hook;
        if (code < 0) return Win32Helper.CallNextHookEx(hook, code, message, dataPointer);
        bool down = message == Win32Helper.WM_KEYDOWN || message == Win32Helper.WM_SYSKEYDOWN;
        bool up = message == Win32Helper.WM_KEYUP || message == Win32Helper.WM_SYSKEYUP;
        if (down || up)
        {
            Win32Helper.KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<Win32Helper.KBDLLHOOKSTRUCT>(dataPointer);
            bool changed = false;
            lock (_gate)
            {
                if (_enabled && !_progress.Paused && _filter.Process((int)data.vkCode, down, (data.flags & Win32Helper.LLKHF_INJECTED) != 0))
                { _progress.AddEnergy(1, DateTime.Now); changed = true; }
            }
            if (changed) Changed?.Invoke();
        }
        return Win32Helper.CallNextHookEx(hook, code, message, dataPointer);
    }

    private void CheckHook()
    {
        if (!_enabled || Snapshot.Paused || _disposed) return;
        if (_thread?.IsAlive != true || _hook == IntPtr.Zero) { StartHook(); return; }
        long previous = Volatile.Read(ref _callbackTicks);
        if (!Win32Helper.TrySendTaggedKeyPress(ReservedHotkeyHookService.InternalMaskVirtualKey, new IntPtr(0x44425452), out _)) return;
        // Probe on the next timer tick; injected probe keys cannot add energy.
        long lastProbe = Interlocked.Exchange(ref _probeTicks, previous);
        if (lastProbe != 0 && previous == lastProbe) { StopHook(); StartHook(); }
    }

    public void Flush() => Save();

    private void Save()
    {
        lock (_writeGate)
        {
            _store.Save(Snapshot);
        }
    }

    private static TreeProgress Clone(TreeProgress p) => new()
    {
        DailyGoal = p.DailyGoal, Paused = p.Paused, LocalDay = p.LocalDay,
        TodayKeys = p.TodayKeys, TotalEnergy = p.TotalEnergy, HighestStage = p.HighestStage,
        FruitEnergy = p.FruitEnergy, FruitInventory = p.FruitInventory, HarvestedFruit = p.HarvestedFruit
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Dispose();
        StopHook(); Save();
    }
}
