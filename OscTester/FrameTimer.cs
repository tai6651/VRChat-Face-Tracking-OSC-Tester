using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OscTester;

internal sealed class FrameTimer : WaitHandle
{
    private const uint CreateHighResolution = 0x00000002;
    private const uint SynchronizeAndModify = 0x00100002;

    public FrameTimer()
    {
        SafeWaitHandle = CreateWaitableTimerEx(0, null, CreateHighResolution, SynchronizeAndModify);
        if (SafeWaitHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Run(int framesPerSecond, CancellationToken cancellation, Action send)
    {
        WaitHandle[] handles = [cancellation.WaitHandle, this];
        long start = Stopwatch.GetTimestamp();
        long frame = 0;
        while (!cancellation.IsCancellationRequested)
        {
            send();

            // Keep deadlines tied to the original clock and skip slots missed during a stall.
            long elapsed = Stopwatch.GetTimestamp() - start;
            frame = Math.Max(frame + 1, elapsed * framesPerSecond / Stopwatch.Frequency + 1);
            long remaining = frame * Stopwatch.Frequency / framesPerSecond - elapsed;
            long dueTime = -Math.Max(1, remaining * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            if (!SetWaitableTimer(SafeWaitHandle, ref dueTime, 0, 0, 0, false))
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            if (WaitAny(handles) == 0)
                break;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW",
        CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name,
        uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
