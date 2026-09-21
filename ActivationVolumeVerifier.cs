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
            if (OperatingSystem.IsWindows())
            {
                if (!WindowsVolumeName(stagingRoot).Equals(WindowsVolumeName(extensionRoot), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ScriptException("InstallCrossVolumeStaging: Staging and extension roots are on different filesystems; atomic activation is not available.");
                }

                return;
            }

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
        // glibc's struct stat has a different member order on AArch64: mode
        // follows inode, before link count.  Do not read the x64 layout on an
        // ARM64 container or regular files will be misclassified and same
        // volume checks will compare a mode value as though it were a device.
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (LinuxArm64Stat(path, out LinuxArm64StatBuffer arm64Result) != 0)
            {
                throw new IOException("stat failed for staging-volume verification.");
            }

            return arm64Result.Device;
        }

        if (LinuxStat(path, out LinuxStatBuffer result) != 0)
        {
            throw new IOException("stat failed for staging-volume verification.");
        }

        return result.Device;
    }

    // Resolve a mounted volume's root and then its unique volume-GUID path.
    // Drive letters and format serials are not sufficient identity proofs for
    // the Directory.Move atomic-activation boundary on Windows.
    private static string WindowsVolumeName(string path)
    {
        System.Text.StringBuilder volumePath = new(32768);
        System.Text.StringBuilder volumeName = new(32768);
        if (!GetVolumePathName(path, volumePath, checked((uint)volumePath.Capacity))
            || !GetVolumeNameForVolumeMountPoint(volumePath.ToString(), volumeName, checked((uint)volumeName.Capacity)))
        {
            throw new IOException("Windows volume identity lookup failed.");
        }

        return volumeName.ToString();
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

    // Linux AArch64: st_dev, st_ino, st_mode, st_nlink, ...
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxArm64StatBuffer
    {
        public ulong Device;
        public ulong Inode;
        public uint Mode;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int DarwinStat(string path, out DarwinStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxStat(string path, out LinuxStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxArm64Stat(string path, out LinuxArm64StatBuffer buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, System.Text.StringBuilder volumePath, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, System.Text.StringBuilder volumeName, uint bufferLength);
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
            if (OperatingSystem.IsWindows())
            {
                EnsureWindowsRegularFile(path);
                return;
            }

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

    private static void EnsureWindowsRegularFile(string path)
    {
        // NTFS/ReFS do not expose Unix FIFOs or device nodes as package-tree
        // entries. Reparse points are the equivalent path-redirection boundary
        // and must be rejected before the file is hashed or copied. Directories
        // and device objects are rejected as well; normal metadata bits such as
        // Archive and ReadOnly remain valid regular files.
        FileAttributes attributes = File.GetAttributes(path);
        const FileAttributes disallowed = FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint;
        if ((attributes & disallowed) != 0)
        {
            throw new ScriptException("InstallPackageObjectNotRegular: Packages may contain regular files only; reparse, device, and directory objects are rejected.");
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
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (LinuxArm64Stat(path, out LinuxArm64StatBuffer arm64Result) != 0)
            {
                throw new IOException("stat failed for package object.");
            }

            return arm64Result.Mode;
        }

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

    // Linux AArch64: st_dev, st_ino, st_mode, st_nlink, ...
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxArm64StatBuffer
    {
        public ulong Device;
        public ulong Inode;
        public uint Mode;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int DarwinStat(string path, out DarwinStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxStat(string path, out LinuxStatBuffer buffer);

    [DllImport("libc.so.6", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int LinuxArm64Stat(string path, out LinuxArm64StatBuffer buffer);
}
