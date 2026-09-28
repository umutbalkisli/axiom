using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axiom.Hosting;

namespace Axiom.LocalSecrets;

/// <summary>
/// The operating system's store for secret values. Keys are opaque strings chosen by <see cref="LocalSecretStore"/>.
/// </summary>
internal interface ISecureStorage
{
    void Write(string key, string value);

    string? Read(string key);

    void Delete(string key);
}

internal static class SecureStorage
{
    /// <summary>
    /// The secure store of this system, or one that refuses every write when there is none (values are never kept in
    /// plain text).
    /// </summary>
    public static ISecureStorage ForThisSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiStorage();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainStorage();
        }

        return SecretToolStorage.IsAvailable() ? new SecretToolStorage() : new UnavailableStorage();
    }
}

/// <summary>
/// Finds the system's store only when a value is actually needed (on Linux that means starting secret-tool), so a
/// program run with no local secrets never touches it.
/// </summary>
internal sealed class LazySecureStorage : ISecureStorage
{
    private readonly Lazy<ISecureStorage> _storage = new(SecureStorage.ForThisSystem);

    public void Write(string key, string value) => _storage.Value.Write(key, value);

    public string? Read(string key) => _storage.Value.Read(key);

    public void Delete(string key) => _storage.Value.Delete(key);
}

internal sealed class UnavailableStorage : ISecureStorage
{
    public void Write(string key, string value) =>
        throw new InvalidOperationException("Secure storage is not available on this system (on Linux, install libsecret's secret-tool).");

    public string? Read(string key) => null;

    public void Delete(string key)
    {
    }
}

/// <summary>
/// Windows: values encrypted with DPAPI for the current user, one file each in the app data folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiStorage : ISecureStorage
{
    private static readonly byte[] Entropy = "Axiom local secrets"u8.ToArray();

    public void Write(string key, string value)
    {
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathOf(key), protectedBytes);
    }

    public string? Read(string key)
    {
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;    // written by another Windows account: treated as missing, the run reports it
        }
    }

    public void Delete(string key) => File.Delete(PathOf(key));

    private static string PathOf(string key)
    {
        var directory = AppData.PathOf("secrets");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, key + ".bin");
    }
}

/// <summary>
/// macOS: generic passwords in the user's login keychain, through Security.framework (no command line, so a value
/// never shows up in the process list).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class KeychainStorage : ISecureStorage
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const int ItemNotFound = -25300;
    private static readonly byte[] Service = "Axiom local secrets"u8.ToArray();

    public void Write(string key, string value)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var data = Encoding.UTF8.GetBytes(value);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var existing, out var item);
        if (status == 0)
        {
            SecKeychainItemFreeContent(IntPtr.Zero, existing);
            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data));
            }
            finally
            {
                CFRelease(item);
            }

            return;
        }

        Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, (uint)data.Length, data, IntPtr.Zero));
    }

    public string? Read(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out var length, out var data, out var item);
        if (status == ItemNotFound)
        {
            return null;
        }

        Check(status);
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    public void Delete(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
        if (status == ItemNotFound)
        {
            return;
        }

        Check(status);
        SecKeychainItemFreeContent(IntPtr.Zero, data);
        try
        {
            Check(SecKeychainItemDelete(item));
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"The keychain refused the operation (OSStatus {status}).");
        }
    }

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(IntPtr cf);
}

/// <summary>
/// Linux: the Secret Service (GNOME Keyring, KWallet) through libsecret's <c>secret-tool</c>. Values go through
/// standard input, never the command line.
/// </summary>
internal sealed class SecretToolStorage : ISecureStorage
{
    public static bool IsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("secret-tool", "--version") { RedirectStandardOutput = true, RedirectStandardError = true });
            process?.WaitForExit(3000);
            return process is { HasExited: true };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public void Write(string key, string value)
    {
        var (exitCode, _, error) = Run(["store", "--label=Axiom local secret", "app", "axiom", "key", key], value);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"secret-tool could not store the secret: {error.Trim()}");
        }
    }

    public string? Read(string key)
    {
        var (exitCode, output, _) = Run(["lookup", "app", "axiom", "key", key], null);
        return exitCode == 0 ? output : null;
    }

    public void Delete(string key) => Run(["clear", "app", "axiom", "key", key], null);

    private static (int ExitCode, string Output, string Error) Run(string[] arguments, string? input)
    {
        var start = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("secret-tool could not be started.");
        if (input is not null)
        {
            process.StandardInput.Write(input);
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
