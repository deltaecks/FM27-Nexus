using System.Runtime.InteropServices;
using FM27Nexus.Core.Models;

namespace FM27Nexus.Core.Paths;

/// <summary>
/// Resolves the Windows Documents Known Folder (OneDrive-safe) and FM27 Sports Interactive layout.
/// </summary>
public sealed class DocumentsPathResolver
{
    public const string SportsInteractiveRelative = @"Sports Interactive\Football Manager 27";

    private static readonly Guid FolderIdDocuments = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    public string GetDocumentsFolder()
    {
        var hr = SHGetKnownFolderPath(FolderIdDocuments, 0, IntPtr.Zero, out var pathPtr);
        if (hr == 0 && pathPtr != IntPtr.Zero)
        {
            try
            {
                var path = Marshal.PtrToStringUni(pathPtr);
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public string GetDefaultFm27Root() =>
        Path.Combine(GetDocumentsFolder(), SportsInteractiveRelative);

    public Fm27DocumentsLayout Resolve(string? manualOverride = null)
    {
        var root = string.IsNullOrWhiteSpace(manualOverride)
            ? GetDefaultFm27Root()
            : Path.GetFullPath(manualOverride.Trim());

        return new Fm27DocumentsLayout { Root = root };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}
