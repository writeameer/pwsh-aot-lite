using System.Runtime.InteropServices;

namespace PwshAotLite;

// Directory.Move is atomic only within a filesystem. Staging lives outside the
// recursively scanned extension root, so this narrow platform seam proves the
// sibling staging directory is still on the destination filesystem before it
// can become an activation candidate.
internal interface IActivationVolumeVerifier
{
    void EnsureSameVolume(string stagingRoot, string extensionRoot);
}

internal sealed class SystemActivationVolumeVerifier : IActivationVolumeVerifier
{
    public void EnsureSameVolume(string stagingRoot, string extensionRoot)
    {
        try
        {
            ulong stagingDevice = OperatingSystem.IsMacOS()
                ? DarwinDeviceId(stagingRoot)
                : OperatingSystem.IsLinux()
                    ? LinuxDeviceId(stagingRoot)
                    : throw new ScriptException("InstallSameVolumeUnavailable: This host has no reviewed filesystem-volume verifier.");
            ulong destinationDevice = OperatingSystem.IsMacOS()
                ? DarwinDeviceId(extensionRoot)
                : OperatingSystem.IsLinux()
                    ? LinuxDeviceId(extensionRoot)
                    : throw new ScriptException("InstallSameVolumeUnavailable: This host has no reviewed filesystem-volume verifier.");
            if (stagingDevice != destinationDevice)
            {
                throw new ScriptException("InstallCrossVolumeStaging: Staging and extension roots are on different filesystems; atomic activation is not available.");
            }
        }
        catch (ScriptException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            throw new ScriptException("InstallSameVolumeUnavailable: The host could not verify atomic activation volume identity.");
        }
    }

    private static ulong DarwinDeviceId(string path)
    {
        if (DarwinStat(path, out DarwinStatBuffer result) != 0)
        {
            throw new IOException("stat failed for staging-volume verification.");
        }

        return unchecked((uint)result.Device);
    }

    private static ulong LinuxDeviceId(string path)
    {
        if (LinuxStat(path, out LinuxStatBuffer result) != 0)
        {
            throw new IOException("stat failed for staging-volume verification.");
        }

        return result.Device;
    }

    // Darwin dev_t is a 32-bit signed value at the beginning of struct stat.
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct DarwinStatBuffer
    {
        public int Device;
        public ushort Mode;
    }

    // Linux dev_t is an unsigned 64-bit value at the beginning of struct stat.
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxStatBuffer
    {
        public ulong Device;
        public ulong Inode;
        public ulong LinkCount;
        public uint Mode;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int DarwinStat(string path, out DarwinStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxStat(string path, out LinuxStatBuffer buffer);
}

// A file pathname is not automatically a regular file on Unix. Do not hash or
// copy FIFOs, devices, sockets, or directories: opening one can block or cross
// a security boundary before package validation has even begun.
internal static class NativeFileObjectPolicy
{
    private const uint FileTypeMask = 0xF000;
    private const uint RegularFile = 0x8000;

    internal static void EnsureRegularFile(string path)
    {
        try
        {
            uint mode = OperatingSystem.IsMacOS()
                ? DarwinMode(path)
                : OperatingSystem.IsLinux()
                    ? LinuxMode(path)
                    : throw new ScriptException("InstallPackageObjectUnsupported: This host has no reviewed file-object classifier.");
            if ((mode & FileTypeMask) != RegularFile)
            {
                throw new ScriptException("InstallPackageObjectNotRegular: Packages may contain regular files only; FIFO, device, socket, and directory objects are rejected.");
            }
        }
        catch (ScriptException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            throw new ScriptException("InstallPackageObjectUnreadable: Package file type could not be classified safely.");
        }
    }

    private static uint DarwinMode(string path)
    {
        if (DarwinStat(path, out DarwinStatBuffer result) != 0)
        {
            throw new IOException("stat failed for package object.");
        }

        return result.Mode;
    }

    private static uint LinuxMode(string path)
    {
        if (LinuxStat(path, out LinuxStatBuffer result) != 0)
        {
            throw new IOException("stat failed for package object.");
        }

        return result.Mode;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct DarwinStatBuffer
    {
        public int Device;
        public ushort Mode;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxStatBuffer
    {
        public ulong Device;
        public ulong Inode;
        public ulong LinkCount;
        public uint Mode;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int DarwinStat(string path, out DarwinStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxStat(string path, out LinuxStatBuffer buffer);
}
