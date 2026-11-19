using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The guest's last Join Friend details, kept per Windows user outside the
/// installation. The password is stored only on request, encrypted with DPAPI
/// for the current Windows user, so the file is useless on another account or PC.
/// Ported from stefanrows/OfflineDAoC (Join Friend, 0.60.0); hand port from rambodolphin13/DividedRealms 3c6cc9d.
/// </summary>
public sealed class JoinFriendProfile
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OfflineDAoC Join Friend v1");

    public string HostAddress { get; init; } = string.Empty;
    public string GuestAccount { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool RememberPassword { get; init; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineDAoC", "join-friend.json");

    public static JoinFriendProfile Load(string path)
    {
        StoredProfile? stored;
        try
        {
            if (!File.Exists(path))
                return new JoinFriendProfile();
            stored = JsonSerializer.Deserialize<StoredProfile>(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new JoinFriendProfile();
        }
        if (stored == null)
            return new JoinFriendProfile();

        string password = TryUnprotect(stored.ProtectedPassword);
        return new JoinFriendProfile
        {
            HostAddress = stored.HostAddress ?? string.Empty,
            GuestAccount = stored.GuestAccount ?? string.Empty,
            Password = password,
            RememberPassword = password.Length > 0,
        };
    }

    public static void Save(string path, string hostAddress, string guestAccount, string password, bool rememberPassword)
    {
        var stored = new StoredProfile
        {
            HostAddress = hostAddress,
            GuestAccount = guestAccount,
            ProtectedPassword = rememberPassword && password.Length > 0 ? Protect(password) : null,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string Protect(string password)
    {
        byte[] plain = Encoding.UTF8.GetBytes(password);
        try
        {
            return Convert.ToBase64String(Dpapi.Protect(plain, Entropy));
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private static string TryUnprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword))
            return string.Empty;
        try
        {
            byte[] plain = Dpapi.Unprotect(Convert.FromBase64String(protectedPassword), Entropy);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                Array.Clear(plain);
            }
        }
        catch (Exception exception) when (exception is FormatException or Win32Exception)
        {
            // Another Windows user, another PC or a damaged value: ask again.
            return string.Empty;
        }
    }

    private sealed class StoredProfile
    {
        public string? HostAddress { get; set; }
        public string? GuestAccount { get; set; }
        public string? ProtectedPassword { get; set; }
    }

    private static class Dpapi
    {
        private const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] plain, byte[] entropy) => Transform(plain, entropy, protect: true);

        public static byte[] Unprotect(byte[] cipher, byte[] entropy) => Transform(cipher, entropy, protect: false);

        private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
        {
            GCHandle inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            GCHandle entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            var inputBlob = new DataBlob { Size = input.Length, Data = inputHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Size = entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
            DataBlob output = default;
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref inputBlob, "Offline DAoC Join Friend", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
                inputHandle.Free();
                entropyHandle.Free();
            }
        }
    }
}
