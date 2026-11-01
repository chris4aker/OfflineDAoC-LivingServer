using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Connection;
using DOL.Network;
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
    /// login port plus BaseServer.PASSPORT_DESK_PORT_OFFSET, and every call needs the caller's own account and password.
    /// One call runs at a time, and never for an account that is logged in, so the rows it changes aren't in use.
    /// </summary>
    public static class PassportDesk
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private const long MAX_PASSPORT_BYTES = 64L * 1024 * 1024;
        private const string TEST_SWITCH = "DIVIDED_REALMS_PASSPORT_DESK"; // Opens the Desk on a private test copy that listens on this PC only.
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static PassportWorld _world;
        private static WebApplication _app;

        public static void Start(GameServerConfiguration config)
        {
            bool hosting = config.IP.Equals(IPAddress.Any);

            if ((!hosting && Environment.GetEnvironmentVariable(TEST_SWITCH) == null) || config.DBType is not EConnectionType.DATABASE_SQLITE)
                return;

            try
            {
                _world = PassportWorld.Live(new SQLiteConnectionStringBuilder(config.DBConnectionString).DataSource, config.ServerName);

                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Listen(config.IP, config.Port + BaseServer.PASSPORT_DESK_PORT_OFFSET);
                    options.Limits.MaxRequestBodySize = MAX_PASSPORT_BYTES;
                });

                _app = builder.Build();
                _app.MapPost("/passport/arrive", context => Guarded(context, true, Arrive));
                _app.MapGet("/passport/visitors", context => Guarded(context, true, Visitors));
                _app.MapPost("/passport/depart", context => Guarded(context, false, Depart));
                _app.MapPost("/passport/depart/confirm", context => Guarded(context, false, Confirm));
                _app.StartAsync().GetAwaiter().GetResult();

                if (log.IsInfoEnabled)
                    log.Info($"Passport Desk is open on port {config.Port + BaseServer.PASSPORT_DESK_PORT_OFFSET}");
            }
            catch (Exception e)
            {
                // Hosting works without the Desk; only bringing a character along doesn't.
                if (log.IsErrorEnabled)
                    log.Error("The Passport Desk could not start", e);
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

        private static async Task Guarded(HttpContext context, bool mayCreateAccount, Func<HttpContext, DbAccount, Task> call)
        {
            await Gate.WaitAsync();

            try
            {
                string address = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? string.Empty;
                DbAccount account = TryReadBasicAuthorization(context.Request.Headers.Authorization, out string name, out string password)
                    ? Authenticate(name, password, address, mayCreateAccount)
                    : null;

                if (account == null)
                {
                    if (log.IsInfoEnabled)
                        log.Info($"Passport Desk refused a login from {address}");

                    await Refuse(context, StatusCodes.Status401Unauthorized, "login", "Your account name or password is wrong for this world.");
                    return;
                }

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

        private static DbAccount Authenticate(string name, string password, string address, bool mayCreate)
        {
            DbAccount account = DOLDB<DbAccount>.FindObjectByKey(name);

            if (account != null)
            {
                string stored = account.Password.StartsWith("##") ? account.Password : LoginRequestHandler.CryptPassword(account.Password);
                return LoginRequestHandler.CryptPassword(password).Equals(stored) ? account : null;
            }

            GameServerConfiguration config = GameServer.Instance.Configuration;

            if (!mayCreate || password.Length == 0 || !config.AutoAccountCreation || !Properties.ALLOW_AUTO_ACCOUNT_CREATION ||
                !HostPassword.AllowsNewAccount(config.HostPasswordHash, password, address))
                return null;

            account = new DbAccount
            {
                Name = name,
                Password = LoginRequestHandler.CryptPassword(password),
                Realm = 0,
                CreationDate = DateTime.Now,
                LastLogin = DateTime.Now,
                LastLoginIP = address,
                Language = Properties.SERV_LANGUAGE,
                PrivLevel = 1,
            };
            GameServer.Database.AddObject(account);

            if (log.IsInfoEnabled)
                log.Info($"New account created at the Passport Desk: {name}");

            return account;
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
