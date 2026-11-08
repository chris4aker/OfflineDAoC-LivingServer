# Play with friends

Living Server 1.2 lets a few friends (about one to four) play in your world. It is experimental: it passes automated
tests, but nobody has played it between two real PCs yet. This is private co-op, not a public server.

One of you **hosts**. Your server runs as usual and friends join your world. The others **join**. They start only their
game client, against your server. The host sends an **invite code** and a **password**. Friends paste the code in. No
outside service is involved: the code simply holds the host's addresses.

Hand port from rambodolphin13's [DividedRealms](https://github.com/rambodolphin13/DividedRealms) fork, with thanks. Join
Friend first came from [stefanrows/OfflineDAoC](https://github.com/stefanrows/OfflineDAoC).

## What to know first

- **You all need the same game and the same Living Server version.** A code made for another game is refused.
- **Characters stay in the host's world.** A friend makes a new character on your server. It is saved in your world, not
  theirs. Taking your own character along is not part of 1.2.
- **The password is the gate.** While you host, a new account from another PC needs your password. Without it the server
  says "Your account needs an invite". Your own PC never needs it. There is no friend list.
- **Home network and Tailscale by default.** Your router stays closed. Internet hosting is a box you tick, and it is off
  by default.
- **Friends play as players.** Make Me GM only ever changes your own account. Friends never get GM.
- **Bots, XP rates and other settings are server-wide.** Friends play with your settings. Changing a setting in the
  launcher stops the server, which drops everyone.

## Hosting

1. In your launcher, press **HOST FOR FRIENDS**.
2. Type a password for your friends, 6 to 20 characters. Don't reuse one from anywhere else: the game's login is not
   encrypted.
3. Leave **Also let friends join over the internet** unticked if your friends are on your home network or on
   [Tailscale](https://tailscale.com).
4. Press **START HOSTING**. The first time, Windows asks for permission to add the firewall rules. Your server then
   starts. If it was already running, stop and start it.
5. Press **COPY CODE**. Send your friends the code and the password.

Your own ENTER REALM works as before. **STOP HOSTING** takes effect at the server's next start. It also clears the
password. Your earlier settings are saved as `runtime\server\config\serverconfig.before-host-for-friends.xml`.

The firewall rules admit only this game's server (`CoreServer.exe`), only on TCP 10300 and UDP 10400, and only from your
home network and Tailscale (100.64.0.0/10). With internet hosting they admit any address. When you stop hosting, the
rules stay but do nothing, because the server listens on your PC alone. To remove them, run
`runtime\host-for-friends-firewall-remove.cmd` as administrator.

### Internet hosting (opt-in)

Tick the box only for friends who can't use your home network or Tailscale. Then:

- The server asks your router (UPnP) to open TCP 10300 and UDP 10400 while it runs. It closes them again when it stops.
- The window checks your router first:
  - "Router OK (UPnP)": the code works from the internet.
  - "Your provider shares addresses (CGNAT)": friends outside your home can't reach you. Use Tailscale.
  - "Your router did not answer UPnP": turn UPnP on in the router, or forward the two ports to this PC by hand and put
    your internet address in `<PublicIP>` in `runtime\server\config\serverconfig.xml`.
- Your internet address can change. If friends suddenly can't reach you, open the window and send the new code.
- Anyone who finds the port can try to log in, so the password is what keeps strangers out. After five wrong passwords
  from one address, that address waits 10 minutes. Stop hosting when you're done.

## Joining

1. Your own server doesn't need to run.
2. Press **JOIN FRIEND** in your launcher.
3. Paste the invite code. A plain host address works too.
4. Choose your own account name for their server. Not `offline`: that is the host's own account (every install has one),
   so the window refuses it and suggests a name.
5. For the password, type the host's password. The first time, their server makes your account with it. Use the same
   name and password every time to get back to your characters.
6. Press **JOIN FRIEND**. The window tries each address in the code and starts the client on the first one that answers.

Tick "Remember my password" to skip typing it next time. It is stored encrypted for your Windows user, in
`%LOCALAPPDATA%\OfflineDAoC\join-friend.json`.

Join Friend uses the original game client. It does not work with the new (Godot) client yet.

## If it doesn't connect

| Message | Check |
|---|---|
| That invite code isn't right | Copy the whole code, starting with LS. |
| This code is for a different game | You both need the same game. |
| Cannot reach your friend's server | Their server has finished starting and they are hosting. Ask for a new code. |
| Your account needs an invite | The host isn't hosting with a password. They press START HOSTING and restart the server. |
| Your password is incorrect | First time: the host's password. After that: the password you used the first time. |
| Service not available | Five wrong passwords in a row. Wait 10 minutes. |
| Logged in, but stuck when entering the world or changing region | Tell us: it is the UDP side (port 10400) or the region address. Session details are in `runtime\logs\client-diagnostics.log` (look for `remote10300`). |

## Reporting a problem

Open an issue with the **Play with friends report** form on GitHub (New issue). Say whether you hosted or joined, how you
were connected, your Windows version and what happened. Don't paste your `account.txt`, a password, an IP address, an
invite code or your database.

## What changes for friends on the server

- **Idle time-outs.** A friend on your home network or Tailscale is treated like your own PC. They are not dropped when
  their client stops pinging, for example while standing still in `/grind`. A friend from a public internet address
  keeps the normal time-outs.
- **One router, many friends.** Two friends behind one router can each make an account at once. The old "one new account
  per address every 15 minutes" rule no longer applies to the home network, Tailscale, your own PC, or anyone with the
  host password.

## How it works (for developers)

- Hosting sets `IP` and `UdpIP` to `0.0.0.0` in `serverconfig.xml` and leaves `RegionIP` at `127.0.0.1`. The server tells
  each client the address that client connected to (`RegionAddress.ForClient`, used by the packet libraries 1.68, 1.73,
  1.124 and 1.126). This PC gets 127.0.0.1; a friend gets the Tailscale or home-network address; a friend from the
  internet gets `PublicIP`.
- `HostPasswordHash` in `serverconfig.xml` guards account creation (`HostPassword`, `LoginRequestHandler`). A new account
  from any address but loopback needs the host password; with no hash set, none is made. Existing accounts are
  unaffected. The launcher compiles the same `HostPassword.cs`, so the hashes match. `HostPasswordAttempts` counts wrong
  host passwords per address, in memory.
- `NetworkTrust` (CoreBase) says which addresses are trusted: loopback, 10/8, 172.16/12, 192.168/16 and 100.64/10.
  `BaseClient.IsTrustedNetworkConnection` is set once when the client connects. `ClientTimeoutPolicy` gives trusted
  clients the loopback client's exemptions (ping time-out, position time-out, idle kick). The same-address account rule
  is skipped for trusted addresses and the host password (`HostPassword.SkipsSameAddressWait`).
- `InviteCode` packs up to three IPv4 addresses, the login port and a check into Crockford base 32, with the prefix `LS`.
  Addresses go in the order Tailscale, internet, home network. It holds no password.
- Internet mode sets `EnableUPnP` (and `PublicIP` to `auto`; `DetectRegionIP` stays off). At start the server maps its
  ports to this PC's address on the router's network (`UpnpNat.LocalAddress`), asks for each port on its own, and keeps the
  router's internet address as `PublicIP`. `UpnpPortMappings.Close` removes the mappings at stop.
- The code defaults (since 1.1.2) are loopback with UPnP and `DetectRegionIP` off. `build_release_037.py` asserts that every
  shipped config is like that and, from 1.2, that it carries no `HostPasswordHash` or `PublicIP`. Host for Friends' files
  (`host-for-friends.json`, the firewall scripts, the `before-` backup) never ship.
- Code: `HostForFriends.cs` / `HostForFriendsForm.cs` (host), `InviteCode.cs`, `JoinFriend.cs` / `JoinFriendForm.cs` /
  `JoinFriendProfile.cs` (guest), `GmAccess.cs`, `Program.cs --join`. Tests: `UT_RegionAddress`, `UT_HostPassword`,
  `UT_HostingNetwork` (server), `HostForFriendsTests`, `JoinFriendTests`, `GmAccessTests` (launcher).
