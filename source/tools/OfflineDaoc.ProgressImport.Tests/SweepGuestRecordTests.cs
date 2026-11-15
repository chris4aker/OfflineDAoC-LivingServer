// Living Server 1.2: a play test sweep the old server never ended left a guest record (AccountXCustomParam,
// LivingServer.EpicSweepGuest) that the server would use at its next start to raise the account back to GM. A transfer
// turns GM off for every account, so the record is not carried; the guest's realm it names is put back instead.
using System.Data.SQLite;
using NUnit.Framework;
using OfflineDaoc.ProgressImport;

namespace OfflineDaoc.ProgressImport.Tests;

[TestFixture]
public class SweepGuestRecordTests
{
    static SQLiteConnection World(bool withTables = true)
    {
        var c = new SQLiteConnection("Data Source=:memory:");
        c.Open();
        if (withTables)
            Exec(c, "CREATE TABLE AccountXCustomParam (CustomParamID INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, KeyName TEXT NOT NULL, Value TEXT);" +
                    "CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT PRIMARY KEY, AccountName TEXT, Name TEXT, Realm INTEGER);" +
                    "INSERT INTO DOLCharacters VALUES ('char-1','owner','Manawatch',3), ('char-2','owner','Brannoc',2);" +
                    "INSERT INTO AccountXCustomParam (Name,KeyName,Value) VALUES " +
                    "('owner','LivingServer.EpicSweepGuest','privlevel=3;realm=1;character=char-1;name=Manawatch')," +
                    "('owner','SomeOtherSetting','kept');");
        return c;
    }

    static void Exec(SQLiteConnection c, string sql)
    {
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static object? Scalar(SQLiteConnection c, string sql)
    {
        using var command = c.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Test]
    public void TheKeyIsTheServers() =>
        Assert.That(ImportEngine.SweepGuestRecordKey, Is.EqualTo("LivingServer.EpicSweepGuest"),
            "source/server/GameServer/bots/autonomous/EpicPlaytestSweepGuestRecord.cs, Param");

    [Test]
    public void ASweepRecordIsNotCarried_AndItsRealmIsPutBack()
    {
        using var c = World();
        Assert.That(ImportEngine.ForgetSweepGuestRecords(c), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Scalar(c, "SELECT count(*) FROM AccountXCustomParam WHERE KeyName='LivingServer.EpicSweepGuest'"), Is.EqualTo(0L),
                "the server must not raise the imported account back to GM");
            Assert.That(Scalar(c, "SELECT Value FROM AccountXCustomParam WHERE KeyName='SomeOtherSetting'"), Is.EqualTo("kept"),
                "the account's other settings travel");
            Assert.That(Scalar(c, "SELECT Realm FROM DOLCharacters WHERE DOLCharacters_ID='char-1'"), Is.EqualTo(1L),
                "the guest's home realm (it was left in a raid's realm)");
            Assert.That(Scalar(c, "SELECT Realm FROM DOLCharacters WHERE DOLCharacters_ID='char-2'"), Is.EqualTo(2L),
                "other characters are untouched");
        });
        Assert.That(ImportEngine.ForgetSweepGuestRecords(c), Is.Zero, "nothing left");
    }

    [Test]
    public void AnUnreadableRecordIsStillNotCarried()
    {
        using var c = World();
        Exec(c, "UPDATE AccountXCustomParam SET Value='privlevel=3;realm=9' WHERE KeyName='LivingServer.EpicSweepGuest'");
        Assert.That(ImportEngine.ForgetSweepGuestRecords(c), Is.EqualTo(1));
        Assert.That(Scalar(c, "SELECT Realm FROM DOLCharacters WHERE DOLCharacters_ID='char-1'"), Is.EqualTo(3L), "no realm to put back");
    }

    [Test]
    public void ADatabaseWithoutTheTableIsLeftAlone()
    {
        using var c = World(withTables: false);
        Assert.That(ImportEngine.ForgetSweepGuestRecords(c), Is.Zero);
    }
}
