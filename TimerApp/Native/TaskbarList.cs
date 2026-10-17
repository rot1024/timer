using System.Runtime.InteropServices;

namespace Timer;

// progress bar on task bar
[ComImport]
[Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
[ClassInterface(ClassInterfaceType.None)]
internal class CTaskbarList
{
}

// COM の vtable の順に、使うメソッドまでを並べる（ITaskbarList → ITaskbarList2 → ITaskbarList3）
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
internal interface ITaskbarList3
{
    // ITaskbarList
    void HrInit();
    void AddTab(IntPtr hwnd);
    void DeleteTab(IntPtr hwnd);
    void ActivateTab(IntPtr hwnd);
    void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3
    void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
    void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
}

internal enum TBPFLAG
{
    TBPF_NOPROGRESS = 0,
    TBPF_INDETERMINATE = 0x1,
    TBPF_NORMAL = 0x2,
    TBPF_ERROR = 0x4,
    TBPF_PAUSED = 0x8
}

internal sealed class TaskbarList
{
    private readonly ITaskbarList3 taskbar = (ITaskbarList3)new CTaskbarList();

    public TaskbarList()
    {
        taskbar.HrInit();
    }

    public void SetState(IntPtr hwnd, TBPFLAG state)
    {
        taskbar.SetProgressState(hwnd, state);
    }

    public void SetValue(IntPtr hwnd, ulong current, ulong total)
    {
        taskbar.SetProgressValue(hwnd, current, total);
    }
}
