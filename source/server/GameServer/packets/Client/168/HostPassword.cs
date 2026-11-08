// Also compiled into the launcher (Host for Friends writes the same hash), which has nullable annotations on.
#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DOL.Network;

namespace DOL.GS.PacketHandler.Client.v168
{
    /// <summary>
    /// The password a host sets in Host for Friends. The login packet carries only an account and a password, so a new
    /// account from another PC must be made with the host's password; the friend then logs in with it like any other
    /// account. Accounts that already exist are unaffected, and so is this PC (loopback): single-player automatic account
    /// creation works as before.
    ///
    /// Hand port from rambodolphin13/DividedRealms af1fcf8 ("Hosting: invite codes, one button, host password"), Living
    /// Server 1.2. One change, the owner's decision of 2026-11-08 ("Password is better"): with no host password set, a
    /// new account from another PC is refused (the fork allowed it). The salt is the fork's, so the hashes match.
    /// The launcher compiles this file too (HostForFriends.HashHostPassword).
    /// </summary>
    public static class HostPassword
    {
        private const string Salt = "DividedRealmsHost:";

        public enum NewAccount
        {
            /// <summary>This PC, or another PC that gave the host password.</summary>
            Allowed,
            /// <summary>Another PC, and the host set no password: nobody else may make an account.</summary>
            NoHostPassword,
            /// <summary>Another PC with a password that is not the host's.</summary>
            WrongPassword,
        }

        public static string Hash(string password)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + (password ?? string.Empty)))).ToLowerInvariant();
        }

        public static NewAccount CheckNewAccount(string storedHash, string password, string clientAddress)
        {
            if (IPAddress.TryParse(clientAddress, out IPAddress address) && IPAddress.IsLoopback(NetworkTrust.Normalize(address)))
                return NewAccount.Allowed;

            if (string.IsNullOrEmpty(storedHash))
                return NewAccount.NoHostPassword;

            return Matches(storedHash, password) ? NewAccount.Allowed : NewAccount.WrongPassword;
        }

        public static bool AllowsNewAccount(string storedHash, string password, string clientAddress) =>
            CheckNewAccount(storedHash, password, clientAddress) == NewAccount.Allowed;

        /// <summary>
        /// The upstream rule "one new account per address every 15 minutes" (account / time_between_account_creation_sameip)
        /// would refuse a second friend behind the same router, or a second test client on this PC. It is skipped for this
        /// PC, the home network and Tailscale, and for a new account made with the host password. A public address without
        /// the host password keeps it (it is refused before this anyway). The total per address
        /// (total_accounts_allowed_sameip) still applies to everyone.
        /// </summary>
        public static bool SkipsSameAddressWait(string storedHash, string password, string clientAddress) =>
            NetworkTrust.IsTrusted(clientAddress) || (!string.IsNullOrEmpty(storedHash) && Matches(storedHash, password));

        private static bool Matches(string storedHash, string password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            byte[] given = Encoding.ASCII.GetBytes(Hash(password));
            byte[] stored = Encoding.ASCII.GetBytes(storedHash.Trim().ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(given, stored);
        }
    }

    /// <summary>
    /// Wrong host passwords per address (Living Server 1.2, co-op audit §3: a wrong password had no lockout). After
    /// <see cref="MaxFailures"/> wrong tries within <see cref="Window"/>, that address may not try again until the
    /// window has passed. Only the new-account host password counts; it lives in memory and resets with the server.
    /// </summary>
    public sealed class HostPasswordAttempts
    {
        public const int MaxFailures = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
        public static readonly HostPasswordAttempts Shared = new();

        private readonly object _sync = new();
        private readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.Ordinal);

        public bool IsLockedOut(string address, DateTime nowUtc)
        {
            lock (_sync)
                return Recent(address, nowUtc).Count >= MaxFailures;
        }

        public void RecordFailure(string address, DateTime nowUtc)
        {
            lock (_sync)
            {
                List<DateTime> recent = Recent(address, nowUtc);
                recent.Add(nowUtc);
                _failures[address ?? string.Empty] = recent;

                // Keep the table small: drop addresses with nothing recent.
                if (_failures.Count > 256)
                {
                    foreach (string key in new List<string>(_failures.Keys))
                    {
                        if (Recent(key, nowUtc).Count == 0)
                            _failures.Remove(key);
                    }
                }
            }
        }

        private List<DateTime> Recent(string address, DateTime nowUtc)
        {
            if (!_failures.TryGetValue(address ?? string.Empty, out List<DateTime> all))
                return new List<DateTime>();

            all.RemoveAll(time => nowUtc - time >= Window);
            return all;
        }
    }
}
