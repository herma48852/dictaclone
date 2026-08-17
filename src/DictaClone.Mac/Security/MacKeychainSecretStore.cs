using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DictaClone.Core.Contracts;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Security;

public sealed class MacKeychainSecretStore : ISecretStore
{
    public const string ServiceName = "com.dictaclone.desktop";
    private readonly IMacKeychainApi _native;

    public MacKeychainSecretStore()
        : this(new NativeMacKeychainApi())
    {
    }

    internal MacKeychainSecretStore(IMacKeychainApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public async Task<string?> ReadAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(
            () => _native.Read(name),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(value);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(
            () => _native.Write(name, value),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(
            () => _native.Delete(name),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }
    }
}

internal interface IMacKeychainApi
{
    string? Read(string name);

    void Write(string name, string value);

    void Delete(string name);
}

internal sealed class NativeMacKeychainApi : IMacKeychainApi
{
    private const int Success = 0;
    private const int DuplicateItem = -25299;
    private const int ItemNotFound = -25300;

    public string? Read(string name)
    {
        nint query = CreateIdentityQuery(name);
        try
        {
            Set(query, SecurityConstants.SecReturnData,
                SecurityConstants.BooleanTrue);
            Set(query, SecurityConstants.SecMatchLimit,
                SecurityConstants.SecMatchLimitOne);

            int status = MacNative.SecItemCopyMatching(query, out nint result);
            if (status == ItemNotFound)
            {
                return null;
            }

            EnsureSuccess(status, "read");
            if (result == nint.Zero)
            {
                throw new InvalidOperationException(
                    "macOS Keychain read returned no data.");
            }

            try
            {
                if (MacNative.CFGetTypeID(result) != MacNative.CFDataGetTypeID())
                {
                    throw new InvalidOperationException(
                        "macOS Keychain read returned an unexpected value.");
                }

                int length = checked((int)MacNative.CFDataGetLength(result));
                var bytes = new byte[length];
                try
                {
                    if (length > 0)
                    {
                        Marshal.Copy(
                            MacNative.CFDataGetBytePtr(result),
                            bytes,
                            startIndex: 0,
                            length);
                    }

                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                MacNative.CFRelease(result);
            }
        }
        finally
        {
            MacNative.CFRelease(query);
        }
    }

    public void Write(string name, string value)
    {
        nint query = CreateIdentityQuery(name);
        nint attributes = CreateDictionary();
        byte[] secret = Encoding.UTF8.GetBytes(value);
        nint data = nint.Zero;
        try
        {
            unsafe
            {
                fixed (byte* bytes = secret)
                {
                    data = MacNative.CFDataCreate(
                        nint.Zero,
                        bytes,
                        secret.Length);
                }
            }

            if (data == nint.Zero)
            {
                throw new InvalidOperationException(
                    "The macOS Keychain secret could not be encoded.");
            }

            Set(attributes, SecurityConstants.SecValueData, data);
            int status = MacNative.SecItemUpdate(query, attributes);
            if (status == ItemNotFound)
            {
                Set(query, SecurityConstants.SecValueData, data);
                status = MacNative.SecItemAdd(query, nint.Zero);
                if (status == DuplicateItem)
                {
                    status = MacNative.SecItemUpdate(query, attributes);
                }
            }

            EnsureSuccess(status, "write");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            if (data != nint.Zero)
            {
                MacNative.CFRelease(data);
            }

            MacNative.CFRelease(attributes);
            MacNative.CFRelease(query);
        }
    }

    public void Delete(string name)
    {
        nint query = CreateIdentityQuery(name);
        try
        {
            int status = MacNative.SecItemDelete(query);
            if (status != ItemNotFound)
            {
                EnsureSuccess(status, "delete");
            }
        }
        finally
        {
            MacNative.CFRelease(query);
        }
    }

    private static nint CreateIdentityQuery(string name)
    {
        nint query = CreateDictionary();
        try
        {
            Set(query, SecurityConstants.SecClass,
                SecurityConstants.SecClassGenericPassword);
            SetString(query, SecurityConstants.SecAttrService,
                MacKeychainSecretStore.ServiceName);
            SetString(query, SecurityConstants.SecAttrAccount, name);
            return query;
        }
        catch
        {
            MacNative.CFRelease(query);
            throw;
        }
    }

    private static nint CreateDictionary()
    {
        nint dictionary = MacNative.CFDictionaryCreateMutable(
            nint.Zero,
            capacity: 0,
            SecurityConstants.DictionaryKeyCallbacks,
            SecurityConstants.DictionaryValueCallbacks);
        return dictionary != nint.Zero
            ? dictionary
            : throw new InvalidOperationException(
                "A macOS Keychain query could not be created.");
    }

    private static void SetString(nint dictionary, nint key, string value)
    {
        nint encoded = ObjectiveC.CreateString(value);
        if (encoded == nint.Zero)
        {
            throw new InvalidOperationException(
                "A macOS Keychain attribute could not be encoded.");
        }

        try
        {
            Set(dictionary, key, encoded);
        }
        finally
        {
            MacNative.CFRelease(encoded);
        }
    }

    private static void Set(nint dictionary, nint key, nint value) =>
        MacNative.CFDictionarySetValue(dictionary, key, value);

    private static void EnsureSuccess(int status, string operation)
    {
        if (status != Success)
        {
            throw new InvalidOperationException(
                $"macOS Keychain {operation} failed with status {status}.");
        }
    }

    private static class SecurityConstants
    {
        private static readonly nint SecurityLibrary =
            NativeLibrary.Load(MacNative.Security);
        private static readonly nint CoreFoundationLibrary =
            NativeLibrary.Load(MacNative.CoreFoundation);

        internal static readonly nint SecClass = SecurityObject("kSecClass");
        internal static readonly nint SecClassGenericPassword =
            SecurityObject("kSecClassGenericPassword");
        internal static readonly nint SecAttrService =
            SecurityObject("kSecAttrService");
        internal static readonly nint SecAttrAccount =
            SecurityObject("kSecAttrAccount");
        internal static readonly nint SecValueData =
            SecurityObject("kSecValueData");
        internal static readonly nint SecReturnData =
            SecurityObject("kSecReturnData");
        internal static readonly nint SecMatchLimit =
            SecurityObject("kSecMatchLimit");
        internal static readonly nint SecMatchLimitOne =
            SecurityObject("kSecMatchLimitOne");
        internal static readonly nint BooleanTrue =
            CoreFoundationObject("kCFBooleanTrue");
        internal static readonly nint DictionaryKeyCallbacks =
            NativeLibrary.GetExport(
                CoreFoundationLibrary,
                "kCFTypeDictionaryKeyCallBacks");
        internal static readonly nint DictionaryValueCallbacks =
            NativeLibrary.GetExport(
                CoreFoundationLibrary,
                "kCFTypeDictionaryValueCallBacks");

        private static nint SecurityObject(string name) =>
            Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary, name));

        private static nint CoreFoundationObject(string name) =>
            Marshal.ReadIntPtr(
                NativeLibrary.GetExport(CoreFoundationLibrary, name));
    }
}
