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
    /// The password a host sets in Host for Friends, the gate for new accounts from other PCs. Accounts that already
    /// exist are unaffected, and so is this PC (loopback): single-player automatic account creation works as before.
    ///
    /// Hand port from rambodolphin13/DividedRealms af1fcf8 ("Hosting: invite codes, one button, host password"), Living
    /// Server 1.2, with the owner's changes of 2026-11-08:
    /// - With no host password set, a new account from another PC is refused (the fork allowed it).
    /// - Every friend has an own password; the host password is never an account's password. The login packet has one
    ///   password field (the legacy client sends at most 20 printable ASCII characters), so a friend's first login sends
    ///   <c>~</c> + a 6-character join token + the friend's own password (at most 13 characters): see
    ///   <see cref="Combine"/>. The token is the first 6 Crockford base-32 characters of SHA-256("LivingServerJoin:" +
    ///   the stored host password hash), so the host password itself never travels and the server checks the token from
    ///   the hash it already stores. The account is made with the own password only.
    /// The salt is the fork's, so the hashes match. The launcher compiles this file too.
    /// </summary>
    public static class HostPassword
    {
        private const string Salt = "DividedRealmsHost:";
        private const string TokenSalt = "LivingServerJoin:";
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>The first character of a first-login password from Join Friend.</summary>
        public const char Marker = '~';
        public const int TokenLength = 6;
        /// <summary>The legacy client's password field.</summary>
        public const int MaxSentLength = 20;
        public const int MaxOwnPasswordLength = MaxSentLength - 1 - TokenLength;

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

        /// <summary>The join token for a stored host password hash (what the server checks).</summary>
        public static string TokenForHash(string storedHash)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(TokenSalt + (storedHash ?? string.Empty).Trim().ToLowerInvariant()));
            var token = new StringBuilder(TokenLength);
            int buffer = 0, bits = 0, index = 0;
            while (token.Length < TokenLength)
            {
                buffer = ((buffer << 8) | digest[index++]) & 0xFFFF;
                bits += 8;
                while (bits >= 5 && token.Length < TokenLength)
                {
                    bits -= 5;
                    token.Append(Alphabet[(buffer >> bits) & 31]);
                }
            }
            return token.ToString();
        }

        /// <summary>The join token for the host password itself (what Join Friend sends).</summary>
        public static string TokenForPassword(string hostPassword) => TokenForHash(Hash(hostPassword));

        /// <summary>What Join Friend sends on a first login: <c>~</c>, the join token, then the friend's own password.</summary>
        public static string Combine(string hostPassword, string ownPassword) =>
            Marker + TokenForPassword(hostPassword) + (ownPassword ?? string.Empty);

        /// <summary>Splits a first-login password. False for anything else (a plain password).</summary>
        public static bool TrySplit(string sent, out string token, out string ownPassword)
        {
            token = null;
            ownPassword = null;
            if (string.IsNullOrEmpty(sent) || sent.Length < 1 + TokenLength + 1 || sent[0] != Marker)
                return false;

            token = sent.Substring(1, TokenLength);
            ownPassword = sent.Substring(1 + TokenLength);
            foreach (char character in token)
            {
                if (Alphabet.IndexOf(character) < 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A new account. This PC: allowed, with the password as sent (unchanged). Another PC: it must send
        /// <see cref="Combine"/>'s form with the right token; <paramref name="accountPassword"/> is then the friend's own
        /// password, the only one the account keeps. Null when refused.
        /// </summary>
        public static NewAccount CheckNewAccount(string storedHash, string sent, string clientAddress, out string accountPassword)
        {
            accountPassword = null;
            if (IPAddress.TryParse(clientAddress, out IPAddress address) && IPAddress.IsLoopback(NetworkTrust.Normalize(address)))
            {
                accountPassword = sent;
                return NewAccount.Allowed;
            }

            if (string.IsNullOrEmpty(storedHash))
                return NewAccount.NoHostPassword;

            if (!TrySplit(sent, out string token, out string own) || !TokenMatches(storedHash, token))
                return NewAccount.WrongPassword;

            accountPassword = own;
            return NewAccount.Allowed;
        }

        public static NewAccount CheckNewAccount(string storedHash, string sent, string clientAddress) =>
            CheckNewAccount(storedHash, sent, clientAddress, out _);

        public static bool AllowsNewAccount(string storedHash, string sent, string clientAddress) =>
            CheckNewAccount(storedHash, sent, clientAddress) == NewAccount.Allowed;

        /// <summary>
        /// A login to an account that exists. The password as sent is compared first, as upstream does. Then, for a friend
        /// whose Join Friend still sent the first-login form, its own-password part is compared. The host password part is
        /// never checked or needed here: it only gates new accounts, and it never opens an account by itself.
        /// </summary>
        public static bool MatchesAccount(string storedAccountPassword, string sent, Func<string, string> crypt)
        {
            if (sent == null || storedAccountPassword == null)
                return false;

            if (crypt(sent).Equals(storedAccountPassword))
                return true;

            return TrySplit(sent, out _, out string own) && own.Length > 0 && crypt(own).Equals(storedAccountPassword);
        }

        /// <summary>
        /// The upstream rule "one new account per address every 15 minutes" (account / time_between_account_creation_sameip)
        /// would refuse a second friend behind the same router, or a second test client on this PC. It is skipped for this
        /// PC, the home network and Tailscale, and for a new account made with the host password. A public address without
        /// the host password keeps it (it is refused before this anyway). The total per address
        /// (total_accounts_allowed_sameip) still applies to everyone.
        /// </summary>
        public static bool SkipsSameAddressWait(string storedHash, string sent, string clientAddress) =>
            NetworkTrust.IsTrusted(clientAddress) ||
            (!string.IsNullOrEmpty(storedHash) && TrySplit(sent, out string token, out _) && TokenMatches(storedHash, token));

        private static bool TokenMatches(string storedHash, string token)
        {
            if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(token))
                return false;

            byte[] given = Encoding.ASCII.GetBytes(token);
            byte[] expected = Encoding.ASCII.GetBytes(TokenForHash(storedHash));
            return CryptographicOperations.FixedTimeEquals(given, expected);
        }
    }

    /// <summary>
    /// Wrong join tokens (host passwords) per address (Living Server 1.2, co-op audit §3: a wrong password had no lockout). After
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
