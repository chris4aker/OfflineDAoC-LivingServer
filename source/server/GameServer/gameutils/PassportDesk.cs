// Character passport: the host's Passport Desk. From rambodolphin13/DividedRealms (da92848; credit: rambodolphin13).
// Living Server 1.2 changes: the Desk never creates an account (a guest logs in to the friend's world once, through the
// hosting branch's login and host password); the host can turn visitors off (passport / passport_visitors), which stops
// arrivals but still lets visitors go home; a wrong password waits a second; the world is the server's own resolved
// database path, and every write is backed up first by the shared engine.
using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Connection;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.ServerProperties;
using DOL.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OfflineDaoc.Passport;

namespace DOL.GS
{
    /// <summary>
    /// A hosted world's door for visiting characters (docs/CHARACTER-PASSPORT.md). It listens only while hosting, on the
    /// login port plus <see cref="PortOffset"/>, and every call needs the caller's own account and password.
    /// One call runs at a time, and never for an account that is logged in, so the rows it changes aren't in use.
    /// </summary>
    public static class PassportDesk
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private const long MAX_PASSPORT_BYTES = 64L * 1024 * 1024;
        private const string TEST_SWITCH = "OFFLINEDAOC_PASSPORT_DESK"; // Opens the Desk on a private test copy that listens on this PC only.
        /// <summary>The Desk listens this far above the login port (OfflineDaoc.Passport.PassportDeskClient.PortOffset).</summary>
        public const int PortOffset = 10;
        private static readonly TimeSpan WrongLoginDelay = TimeSpan.FromSeconds(1);
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static PassportWorld _world;
        private static WebApplication _app;

        /// <summary>
        /// Opens while this world is hosted for friends (it listens on more than this PC: the game's IP is not a loopback
        /// address), or on a test copy with the test switch, and only on a SQLite world. Hosting with visitors turned off
        /// still opens it, so visitors already here can go home; it then refuses new arrivals.
        /// </summary>
        public static bool ShouldOpen(IPAddress listen, bool sqlite, bool testSwitch) =>
            sqlite && listen != null && (testSwitch || !IPAddress.IsLoopback(listen));

        public static void Start(GameServerConfiguration config)
        {
            if (!ShouldOpen(config.IP, config.DBType is EConnectionType.DATABASE_SQLITE && WorldDatabaseSafety.DatabasePath != null,
                    Environment.GetEnvironmentVariable(TEST_SWITCH) != null))
                return;

            try
            {
                _world = PassportWorld.Live(WorldDatabaseSafety.DatabasePath, config.ServerName);

                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Listen(config.IP, config.Port + PortOffset);
                    options.Limits.MaxRequestBodySize = MAX_PASSPORT_BYTES;
                });

                _app = builder.Build();
                _app.MapPost("/passport/arrive", context => Guarded(context, Arrive));
                _app.MapGet("/passport/visitors", context => Guarded(context, Visitors));
                _app.MapPost("/passport/depart", context => Guarded(context, Depart));
                _app.MapPost("/passport/depart/confirm", context => Guarded(context, Confirm));
                _app.StartAsync().GetAwaiter().GetResult();

                if (log.IsInfoEnabled)
                    log.Info($"Passport Desk is open on port {config.Port + PortOffset} (visitors {(Properties.PASSPORT_VISITORS ? "welcome" : "turned off: only going home")})");
            }
            catch (Exception e)
            {
                // Hosting works without the Desk; only bringing a character along doesn't.
                if (log.IsErrorEnabled)
                    log.Error("The Passport Desk could not start", e);
            }
        }

        public static void Stop()
        {
            WebApplication app = _app;
            _app = null;

            try
            {
                app?.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                if (log.IsWarnEnabled)
                    log.Warn("The Passport Desk did not stop cleanly", e);
            }
        }

        public static bool TryReadBasicAuthorization(string header, out string account, out string password)
        {
            account = password = null;

            if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            string decoded;

            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            }
            catch (FormatException)
            {
                return false;
            }

            int separator = decoded.IndexOf(':');

            if (separator < 1)
                return false;

            account = decoded[..separator];
            password = decoded[(separator + 1)..];
            return true;
        }

        private static async Task Guarded(HttpContext context, Func<HttpContext, DbAccount, Task> call)
        {
            // The sign-in is checked before the one-call-at-a-time gate, so wrong passwords never hold up anyone else, and
            // the game login's lockout applies here too (audit 2026-11-13).
            string address = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? string.Empty;
            bool otherPc = !HostPassword.IsThisPc(address);

            if (otherPc && HostPasswordAttempts.AccountLogins.IsLockedOut(address, DateTime.UtcNow))
            {
                await Refuse(context, StatusCodes.Status429TooManyRequests, "locked",
                    "Too many wrong passwords from your PC lately. Wait ten minutes and try again.");
                return;
            }

            DbAccount account = TryReadBasicAuthorization(context.Request.Headers.Authorization, out string name, out string password)
                ? Authenticate(name, password)
                : null;

            if (account == null)
            {
                if (otherPc)
                    HostPasswordAttempts.AccountLogins.RecordFailure(address, DateTime.UtcNow);

                if (log.IsInfoEnabled)
                    log.Info($"Passport Desk refused a login from {address}");

                await Task.Delay(WrongLoginDelay);
                await Refuse(context, StatusCodes.Status401Unauthorized, "login",
                    "Your account name or password is wrong for this world. If you have never played there, join it once first.");
                return;
            }

            await Gate.WaitAsync();

            try
            {
                await call(context, account);
            }
            catch (PassportException refusal)
            {
                await Refuse(context, StatusCodes.Status409Conflict, refusal.Code, refusal.Message);
            }
            catch (BadHttpRequestException)
            {
                await Refuse(context, StatusCodes.Status413PayloadTooLarge, "too_large", "That passport is too large.");
            }
            catch (Exception e)
            {
                // No refusal body: the launcher treats this as no answer, because the call may have gone through.
                if (log.IsErrorEnabled)
                    log.Error("Passport Desk call failed", e);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static Task Refuse(HttpContext context, int status, string code, string message)
        {
            context.Response.StatusCode = status;
            return context.Response.WriteAsJsonAsync(new { code, message });
        }

        /// <summary>An existing account with the right password (the login handler's own check). The Desk never makes one.</summary>
        private static DbAccount Authenticate(string name, string password)
        {
            DbAccount account = DOLDB<DbAccount>.FindObjectByKey(name);

            if (account == null || string.IsNullOrEmpty(account.Password) || password.Length == 0)
                return null;

            string stored = account.Password.StartsWith("##") ? account.Password : LoginRequestHandler.CryptPassword(account.Password);
            return LoginRequestHandler.CryptPassword(password).Equals(stored) ? account : null;
        }

        private static void RefuseWhileLoggedIn(DbAccount account)
        {
            if (ClientService.Instance.GetClientFromAccountName(account.Name) != null)
                throw new PassportException("logged_in", "Log out of your friend's world first, and wait a minute if the game closed suddenly.");
        }

        private static void RefuseOthersCharacter(DbAccount account, string characterId)
        {
            foreach (VisitorInfo visitor in PassportEngine.Visitors(_world, account.Name))
            {
                if (visitor.CharacterId == characterId)
                    return;
            }

            throw new PassportException("not_visiting", "That character is not visiting on this account.");
        }

        private static async Task Arrive(HttpContext context, DbAccount account)
        {
            if (!Properties.PASSPORT_VISITORS)
                throw new PassportException("visitors_off", "Your friend has turned visitors off. Characters already here can still go home.");

            RefuseWhileLoggedIn(account);
            string path = Path.Combine(Path.GetTempPath(), $"passport-{Guid.NewGuid():N}.odpassport");

            try
            {
                await using (FileStream file = File.Create(path))
                    await context.Request.Body.CopyToAsync(file);

                string visitingName = context.Request.Query["name"];
                ArrivalReport report = PassportEngine.Arrive(_world, path, account.Name, visitingName);

                if (log.IsInfoEnabled)
                    log.Info($"Passport Desk: {report.Name} arrived on account {account.Name}");

                await context.Response.WriteAsJsonAsync(report);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static Task Visitors(HttpContext context, DbAccount account)
        {
            return context.Response.WriteAsJsonAsync(PassportEngine.Visitors(_world, account.Name));
        }

        private static async Task Depart(HttpContext context, DbAccount account)
        {
            RefuseWhileLoggedIn(account);
            string characterId = context.Request.Query["character"];
            RefuseOthersCharacter(account, characterId);
            long? purse = long.TryParse(context.Request.Query["purse"], out long copper) ? copper : null;
            string folder = Path.Combine(Path.GetTempPath(), $"passport-{Guid.NewGuid():N}");

            try
            {
                string path = PassportEngine.Depart(_world, characterId, purse, folder);
                context.Response.ContentType = "application/octet-stream";
                await context.Response.SendFileAsync(path);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static Task Confirm(HttpContext context, DbAccount account)
        {
            RefuseWhileLoggedIn(account);
            string characterId = context.Request.Query["character"];
            RefuseOthersCharacter(account, characterId);
            PassportEngine.ConfirmDeparture(_world, characterId, context.Request.Query["trip"]);

            if (log.IsInfoEnabled)
                log.Info($"Passport Desk: {characterId} went home from account {account.Name}");

            return Task.CompletedTask;
        }
    }
}
