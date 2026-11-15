using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Opens a file that a <see cref="ResourcePolicy"/> authorised, and checks the file it opened.
/// <see cref="ResourcePolicy.Authorize"/> resolves every symbolic link along a path and judges
/// where it leads; a reader then opened the path again by name. Between the two, a link could be
/// put in place of a file or a directory on the path, and the reader followed it to a file the
/// policy does not allow. Here the file is opened first, and the policy judges the path of the
/// file actually open, which the operating system reports for the handle. A file whose path the
/// platform cannot report is refused when the path to it has changed since it was authorised.
/// </summary>
internal static class AuthorizedFile
{
    /// <summary>
    /// Opens the file at <paramref name="authorized"/>, a <c>file:</c> URI that
    /// <paramref name="policy"/> returned from <see cref="ResourcePolicy.Authorize"/>, for reading.
    /// </summary>
    /// <param name="policy">The policy that authorised it; null where no policy applies.</param>
    /// <param name="authorized">The authorised <c>file:</c> URI.</param>
    /// <param name="access">The kind of access that was authorised.</param>
    /// <param name="requested">What was asked for, for the refusal: never the path it leads to.</param>
    /// <exception cref="ResourceAccessDeniedException">The file open is not one the policy allows.</exception>
    internal static FileStream OpenRead(ResourcePolicy? policy, Uri authorized, ResourceAccessKind access, string requested)
    {
        ArgumentNullException.ThrowIfNull(authorized);
        var path = authorized.LocalPath;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous);
        if (policy is null)
            return stream;
        try
        {
            var opened = OpenedPath(stream.SafeFileHandle);
            var allowed = opened is not null
                ? policy.IsAllowed(new Uri(opened), access)
                : string.Equals(ResourcePolicy.CanonicalPath(path), path, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (allowed)
                return stream;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            // The opened file cannot be placed: refused below, as an unplaceable path is.
        }
        stream.Dispose();
        throw new ResourceAccessDeniedException(requested, access,
            "the file opened is not the one the resource policy allowed");
    }

    /// <summary>The text of an authorised file, decoded as <see cref="File.ReadAllTextAsync(string, CancellationToken)"/> does.</summary>
    internal static async Task<string> ReadAllTextAsync(ResourcePolicy? policy, Uri authorized, ResourceAccessKind access,
        string requested, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var stream = OpenRead(policy, authorized, access, requested);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = encoding is null ? new StreamReader(stream, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The text of an authorised file, read synchronously.</summary>
    internal static string ReadAllText(ResourcePolicy? policy, Uri authorized, ResourceAccessKind access, string requested)
    {
        using var stream = OpenRead(policy, authorized, access, requested);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>The bytes of an authorised file.</summary>
    internal static async Task<byte[]> ReadAllBytesAsync(ResourcePolicy? policy, Uri authorized, ResourceAccessKind access,
        string requested, CancellationToken cancellationToken = default)
    {
        var stream = OpenRead(policy, authorized, access, requested);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// The path of the file <paramref name="handle"/> has open, with every link resolved, as the
    /// operating system reports it; null where it does not.
    /// </summary>
    internal static string? OpenedPath(SafeFileHandle handle)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
        {
            var fd = handle.DangerousGetHandle().ToInt32();
            return new FileInfo($"/proc/self/fd/{fd}").LinkTarget;
        }
        if (OperatingSystem.IsWindows())
            return WindowsFinalPath(handle);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            return AppleFinalPath(handle);
        return null;
    }

    private static string? WindowsFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        }
        if (length == 0 || length > buffer.Length)
            return null;
        var path = new string(buffer, 0, (int)length);
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }

    private static string? AppleFinalPath(SafeFileHandle handle)
    {
        const int F_GETPATH = 50;
        var buffer = new byte[1024]; // MAXPATHLEN
        if (fcntl(handle.DangerousGetHandle().ToInt32(), F_GETPATH, buffer) == -1)
            return null;
        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("libc", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int fcntl(int fd, int cmd, [Out] byte[] buffer);
}
