using System;
using System.Linq;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

// HearthDAoC: ScriptMgr.LoadCommands puts every command name and alias into one case-blind dictionary, so a name two
// commands claim throws at start, and whichever loads second loses it. /transfercorpse's "/tc" alias did that to
// OfflineDAoC's /tc (teleport to the Realm Exchange); in the other load order the Realm Exchange command would vanish.
[TestFixture]
public sealed class UT_CommandNames
{
    private static readonly (string Name, Type Handler)[] Names = typeof(ScriptMgr).Assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && typeof(ICommandHandler).IsAssignableFrom(type))
        .SelectMany(type => type.GetCustomAttributes(typeof(CmdAttribute), false).Cast<CmdAttribute>()
            .SelectMany(attribute => new[] { attribute.Cmd }.Concat(attribute.Aliases ?? []))
            .Select(name => (name, type)))
        .ToArray();

    [Test]
    public void EveryCommandNameAndAliasBelongsToOneCommand()
    {
        string[] shared = Names
            .GroupBy(entry => entry.Name, StringComparer.InvariantCultureIgnoreCase)
            .Where(group => group.Select(entry => entry.Handler).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.Handler.Name))}")
            .ToArray();
        Assert.That(shared, Is.Empty);
    }

    [Test]
    public void SlashTcIsTheRealmExchangeTeleport()
    {
        Assert.That(Names.Where(entry => entry.Name == "&tc").Select(entry => entry.Handler),
            Is.EqualTo(new[] { typeof(TeleportToExchangeCommandHandler) }));
        Assert.That(Names.Where(entry => entry.Handler == typeof(transfercorpseCommandHandler)).Select(entry => entry.Name),
            Is.EqualTo(new[] { "&transfercorpse" }));
    }
}
