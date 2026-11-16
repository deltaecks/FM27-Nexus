using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FM27Nexus.Core.Memory.Live;

public sealed class NativeProcessMemory : IDisposable
{
    private IntPtr _handle;

    public int ProcessId { get; private set; }
    public bool IsOpen => _handle != IntPtr.Zero;

    public void Open(int processId)
    {
        Close();
        _handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryInformation, false, processId);
        if (_handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed. Is MemoryAgent elevated?");
        ProcessId = processId;
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }

        ProcessId = 0;
    }

    public ModuleInfo? FindModule(string moduleFileName)
    {
        var proc = Process.GetProcessById(ProcessId);
        foreach (ProcessModule mod in proc.Modules)
        {
            if (mod.ModuleName.Equals(moduleFileName, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(mod.FileName).Equals(moduleFileName, StringComparison.OrdinalIgnoreCase))
            {
                return new ModuleInfo(mod.ModuleName, mod.BaseAddress.ToInt64(), mod.ModuleMemorySize, mod.FileName);
            }
        }

        return null;
    }

    public byte[] ReadBytes(long address, int size)
    {
        var buffer = new byte[size];
        if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, size, out var read) || read != size)
            throw new InvalidOperationException($"ReadProcessMemory failed at 0x{address:X}.");
        return buffer;
    }

    public bool TryReadBytes(long address, Span<byte> buffer)
    {
        var tmp = new byte[buffer.Length];
        if (!ReadProcessMemory(_handle, (IntPtr)address, tmp, tmp.Length, out var read) || read != tmp.Length)
            return false;
        tmp.CopyTo(buffer);
        return true;
    }

    public void WriteBytes(long address, byte[] data)
    {
        if (!WriteProcessMemory(_handle, (IntPtr)address, data, data.Length, out var written) || written != data.Length)
            throw new InvalidOperationException($"WriteProcessMemory failed at 0x{address:X}.");
    }

    public long ReadInt64(long address) => BitConverter.ToInt64(ReadBytes(address, 8), 0);
    public int ReadInt32(long address) => BitConverter.ToInt32(ReadBytes(address, 4), 0);
    public short ReadInt16(long address) => BitConverter.ToInt16(ReadBytes(address, 2), 0);
    public byte ReadByte(long address) => ReadBytes(address, 1)[0];

    public IEnumerable<MemoryBasicInfo> EnumerateCommittedRegions(bool writableOnly = false)
    {
        long address = 0;
        while (address >= 0 && address < 0x00007FFFFFFFFFFF)
        {
            var mbi = new MemoryBasicInformation();
            var result = VirtualQueryEx(_handle, (IntPtr)address, out mbi, (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>());
            if (result == UIntPtr.Zero)
                yield break;

            if (mbi.State == MemCommit && (mbi.Protect & PageGuard) == 0 && mbi.Protect != PageNoAccess)
            {
                var readable = (mbi.Protect & (PageReadonly | PageReadwrite | PageWritecopy | PageExecuteRead | PageExecuteReadwrite | PageExecuteWritecopy)) != 0;
                var writable = (mbi.Protect & (PageReadwrite | PageWritecopy | PageExecuteReadwrite | PageExecuteWritecopy)) != 0;
                var regionSize = (long)mbi.RegionSize;
                if (readable && regionSize > 0 && (!writableOnly || writable))
                {
                    yield return new MemoryBasicInfo(mbi.BaseAddress.ToInt64(), regionSize, writable);
                }
            }

            var next = mbi.BaseAddress.ToInt64() + (long)mbi.RegionSize;
            if (next <= address)
                yield break;
            address = next;
        }
    }

    public void Dispose() => Close();

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadonly = 0x02;
    private const uint PageReadwrite = 0x04;
    private const uint PageWritecopy = 0x08;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadwrite = 0x40;
    private const uint PageExecuteWritecopy = 0x80;
    private const uint PageGuard = 0x100;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}

public sealed record ModuleInfo(string Name, long BaseAddress, int Size, string FilePath);
public sealed record MemoryBasicInfo(long BaseAddress, long Size, bool Writable = false);
