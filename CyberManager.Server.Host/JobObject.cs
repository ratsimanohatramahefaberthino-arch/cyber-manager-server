using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CyberManager.Server.Host;

/// <summary>
/// Job Object Windows avec KILL_ON_JOB_CLOSE : si le service meurt (même brutalement),
/// Windows tue tous les processus rattachés. Pas de PHP orphelin qui garde le port.
/// Les processus enfants des enfants (ex. « schedule:run » lancé par « schedule:work ») rejoignent le job automatiquement.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClasse = 9;

    private IntPtr _handle;

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject a échoué");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        var taille = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(taille);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformationClasse, ptr, (uint)taille))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject a échoué");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Rattache un processus au job. Lève une exception si impossible.</summary>
    public void Assigner(Process processus)
    {
        if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(JobObject));

        if (!AssignProcessToJobObject(_handle, processus.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject a échoué");
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            // Fermer le dernier handle du job déclenche KILL_ON_JOB_CLOSE : tous les enfants sont tués.
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    // ------------------------------------------------------------------
    // P/Invoke
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}