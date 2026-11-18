using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Intent = DOL.GS.eAutonomousChatIntent;
using Short = DOL.GS.AutonomousChatShortTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2 bot chat audit fixes, reading and replies (scratchpad chataudit: DESIGN-001..018, 034, 041, 042, 046, 047, CORPUS-C-001,
/// 003, 004, 007, 008, 017, 018, 053, LOG-002, 027, 040, TEXT-010, 043). Tests only: none of this is seen on the owner's PC yet.
/// The intent lines are the fixed two thirds of the design auditor's fresh set (intent_lines.tsv, every line whose index in its
/// intent is not 2 mod 3); the other third stays out of every test and every fix round, and is reported apart.
/// </summary>
[TestFixture]
public class UT_ChatReadingAudit12
{
    // ---- the fixed read set (DESIGN-046) ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Intent[]> Wrong = new()
    {
        ["NotAbuse"] = [Intent.Abuse],
        ["NotGreetFarewell"] = [Intent.Greeting, Intent.Farewell],
        ["NotThanksEtc"] = [Intent.Thanks, Intent.Ding, Intent.Congrats, Intent.Away, Intent.Back, Intent.Laugh],
        ["NotRezAlarm"] = [Intent.Rez, Intent.Alarm],
        ["NotGroupTradeEtc"] = [Intent.Grouping, Intent.Trading],
    };

    private static readonly (string Expect, string Text)[] ReadSet =
    [
        ("Greeting", "hi all"),
        ("Greeting", "hey everyone"),
        ("Greeting", "hello realm"),
        ("Greeting", "morning folks"),
        ("Greeting", "yo"),
        ("Greeting", "hey guys whats up"),
        ("Greeting", "hi there"),
        ("Greeting", "heya all, long day"),
        ("Greeting", "sup all"),
        ("Greeting", "hello again"),
        ("Greeting", "gm all"),
        ("Greeting", "afternoon everyone"),
        ("Greeting", "hi all, just got on"),
        ("Greeting", "hey all how is everyone tonight"),
        ("Greeting", "hello? anyone home"),
        ("Greeting", "hi everybody"),
        ("Greeting", "hey you"),
        ("Greeting", "ello all"),
        ("Greeting", "hi guys im new here"),
        ("Greeting", "hey all back on my cleric tonight"),
        ("Farewell", "gn all"),
        ("Farewell", "night everyone"),
        ("Farewell", "cya tomorrow"),
        ("Farewell", "im off to bed"),
        ("Farewell", "logging for the night"),
        ("Farewell", "later guys"),
        ("Farewell", "time for bed, work tomorrow"),
        ("Farewell", "see you all tomorrow"),
        ("Farewell", "nn all"),
        ("Farewell", "im done for tonight"),
        ("Farewell", "gotta run, wife aggro"),
        ("Farewell", "k logging, cya"),
        ("Farewell", "thats me done, night"),
        ("Farewell", "peace out"),
        ("Farewell", "ttyl"),
        ("Farewell", "im gone, have fun"),
        ("Farewell", "bedtime for me"),
        ("Farewell", "signing off"),
        ("Farewell", "bbl"),
        ("Farewell", "see ya"),
        ("Thanks", "ty"),
        ("Thanks", "thx"),
        ("Thanks", "ty for the rez"),
        ("Thanks", "thx for the group all"),
        ("Thanks", "thanks man"),
        ("Thanks", "thank you so much"),
        ("Thanks", "cheers mate"),
        ("Thanks", "thx for the buffs"),
        ("Thanks", "thanks for the help earlier"),
        ("Thanks", "ty for the port"),
        ("Thanks", "thank u"),
        ("Thanks", "ty all, great group"),
        ("Thanks", "ok thanks"),
        ("Thanks", "thx for the tip"),
        ("Thanks", "tks"),
        ("Thanks", "thanks for the info"),
        ("Thanks", "thnx for the heal"),
        ("Thanks", "many thanks"),
        ("Thanks", "ty for the advice"),
        ("Thanks", "thanks, that worked"),
        ("Congrats", "gz"),
        ("Congrats", "grats"),
        ("Congrats", "congrats on 50"),
        ("Congrats", "gz on the ding"),
        ("Congrats", "nice ding"),
        ("Congrats", "woot gz"),
        ("Congrats", "gz all"),
        ("Congrats", "grats on the epic"),
        ("Congrats", "gz gz"),
        ("Congrats", "grats on rr3"),
        ("Congrats", "gj on the kill"),
        ("Congrats", "grats on the drop"),
        ("Congrats", "gz on the new level"),
        ("Congrats", "grats dude"),
        ("Congrats", "nice one, gz"),
        ("Congrats", "gz on the masterpiece"),
        ("Congrats", "congrats!!"),
        ("Congrats", "gz m8"),
        ("Congrats", "gratz all round"),
        ("Congrats", "grats on the relic"),
        ("Ding", "ding"),
        ("Ding", "ding!"),
        ("Ding", "ding 42 finally"),
        ("Ding", "dinged"),
        ("Ding", "finally 50"),
        ("Ding", "ding ding"),
        ("Ding", "level 20 at last"),
        ("Ding", "i just hit 35"),
        ("Ding", "ding, time to train"),
        ("Ding", "hit level 10"),
        ("Ding", "47 now"),
        ("Ding", "dinged twice tonight"),
        ("Ding", "5 bubs to go then ding"),
        ("Ding", "ding 50!!!!"),
        ("Ding", "got my level"),
        ("Ding", "new level yay"),
        ("Ding", "lvl 33 now"),
        ("Ding", "ding. about time"),
        ("Ding", "i leveled!"),
        ("Ding", "just turned 20"),
        ("Laugh", "lol"),
        ("Laugh", "haha"),
        ("Laugh", "lmao"),
        ("Laugh", "hehe"),
        ("Laugh", "hahaha"),
        ("Laugh", "heh"),
        ("Laugh", "lololol"),
        ("Laugh", ":)"),
        ("Laugh", "lol wow"),
        ("Laugh", "haha yeah"),
        ("Laugh", "bwahaha"),
        ("Laugh", "lol true"),
        ("Laugh", "lol!"),
        ("Laugh", "heh heh"),
        ("Laugh", "ha"),
        ("Laugh", "lol oh no"),
        ("Laugh", "too funny"),
        ("Laugh", "hah good one"),
        ("Laugh", "*laughs*"),
        ("Laugh", "lol classic"),
        ("Away", "brb"),
        ("Away", "afk"),
        ("Away", "afk 5"),
        ("Away", "bio"),
        ("Away", "brb kid crying"),
        ("Away", "afk a sec"),
        ("Away", "back in 5"),
        ("Away", "brb drink"),
        ("Away", "one sec"),
        ("Away", "hold on phone"),
        ("Away", "need 2 min afk"),
        ("Away", "afk for a bit"),
        ("Away", "gimme a minute"),
        ("Away", "brb bathroom"),
        ("Away", "bbiab"),
        ("Away", "be back in ten"),
        ("Away", "brb need coffee"),
        ("Away", "afk, wife calling"),
        ("Away", "afk tabbing out"),
        ("Away", "brb reboot"),
        ("Back", "back"),
        ("Back", "re"),
        ("Back", "back, sorry"),
        ("Back", "ok back"),
        ("Back", "re all"),
        ("Back", "back from dinner"),
        ("Back", "sry im back"),
        ("Back", "returned"),
        ("Back", "k im here"),
        ("Back", "back at keyboard"),
        ("Back", "re, sorry phone"),
        ("Back", "im here again"),
        ("Back", "ok im back for real"),
        ("Back", "here"),
        ("Back", "back all"),
        ("Back", "alright back"),
        ("Back", "bak"),
        ("Back", "back, where were we"),
        ("Back", "re guys"),
        ("Back", "im back, anyone still on"),
        ("Rez", "rez pls"),
        ("Rez", "need a rez"),
        ("Rez", "can i get a rez"),
        ("Rez", "rez please at the bridge"),
        ("Rez", "dead near the gate, rez?"),
        ("Rez", "rez me pls"),
        ("Rez", "res plz"),
        ("Rez", "anyone able to rez me"),
        ("Rez", "need rez at tomb entrance"),
        ("Rez", "rez?"),
        ("Rez", "dead again, rez plz"),
        ("Rez", "a rez would be nice"),
        ("Rez", "looking for a rez at cotswold"),
        ("Rez", "pls rez"),
        ("Rez", "rez at loc 23k 41k pls"),
        ("Rez", "help im dead"),
        ("Rez", "anyone near dartmoor that can rez"),
        ("Rez", "need a res badly"),
        ("Rez", "is there a rezzer in the house"),
        ("Rez", "anybody got rez"),
        ("Alarm", "inc"),
        ("Alarm", "inc 5 mids"),
        ("Alarm", "8 hibs at beno"),
        ("Alarm", "mids at the gate"),
        ("Alarm", "stealthers at pk"),
        ("Alarm", "zerg at mmg"),
        ("Alarm", "inc inc inc"),
        ("Alarm", "add"),
        ("Alarm", "run!"),
        ("Alarm", "big group of hibs heading to emain"),
        ("Alarm", "beno under attack"),
        ("Alarm", "4 mids on the bridge"),
        ("Alarm", "relic keep is being hit"),
        ("Alarm", "inc 2 fg mid"),
        ("Alarm", "stealther on me"),
        ("Alarm", "incoming"),
        ("Alarm", "rams on the door at crim"),
        ("Alarm", "they are in the keep"),
        ("Alarm", "inc from behind"),
        ("Alarm", "20+ albs at hmg"),
        ("ClassTalk", "is friar good?"),
        ("ClassTalk", "best spec for a cleric"),
        ("ClassTalk", "how should i spec my paladin"),
        ("ClassTalk", "are thanes any good in rvr"),
        ("ClassTalk", "is smite cleric viable"),
        ("ClassTalk", "zerkers got nerfed"),
        ("ClassTalk", "what do bards spec"),
        ("ClassTalk", "is a shield worth it on an armsman"),
        ("ClassTalk", "wizards or theurgists for pve"),
        ("ClassTalk", "whats the best race for a scout"),
        ("ClassTalk", "any infils on? need spec advice"),
        ("ClassTalk", "is 50 enhance worth it"),
        ("ClassTalk", "what lines does a cabalist get"),
        ("ClassTalk", "sorc mez is so good"),
        ("ClassTalk", "thinking of making a sm"),
        ("ClassTalk", "how many points in stealth"),
        ("ClassTalk", "does rejuv matter at 50"),
        ("ClassTalk", "skalds are great"),
        ("ClassTalk", "my paladin is so slow to level"),
        ("ClassTalk", "which pet for a cabalist"),
        ("BotCheck", "are you a bot?"),
        ("BotCheck", "r u real"),
        ("BotCheck", "are you guys bots"),
        ("BotCheck", "u a bot?"),
        ("BotCheck", "is anyone here human"),
        ("BotCheck", "are you a real player"),
        ("BotCheck", "you type like a bot"),
        ("BotCheck", "are these all bots"),
        ("BotCheck", "am i talking to a computer"),
        ("BotCheck", "are you human or what"),
        ("BotCheck", "is this server all bots"),
        ("BotCheck", "you are a bot arent you"),
        ("BotCheck", "do you actually play or are you automated"),
        ("BotCheck", "are you an actual person"),
        ("BotCheck", "hello? are you real"),
        ("BotCheck", "are you macroing"),
        ("BotCheck", "are you controlled by the server"),
        ("BotCheck", "be honest, are you a bot"),
        ("BotCheck", "this feels like talking to npcs"),
        ("BotCheck", "is that guy a bot"),
        ("Grouping", "lfg"),
        ("Grouping", "lfg 30 cleric"),
        ("Grouping", "anyone want to group"),
        ("Grouping", "lf1m tank for keltoi"),
        ("Grouping", "any groups for a 24 paladin"),
        ("Grouping", "room for one more?"),
        ("Grouping", "want to duo"),
        ("Grouping", "looking for group, 41 wizard"),
        ("Grouping", "anyone forming for df"),
        ("Grouping", "lf healer for lyonesse"),
        ("Grouping", "group?"),
        ("Grouping", "35 merc lfg pst"),
        ("Grouping", "wanna team up"),
        ("Grouping", "lf2m then we go"),
        ("Grouping", "can i tag along"),
        ("Grouping", "need 1 more for barrows, healer pref"),
        ("Grouping", "invite me"),
        ("Grouping", "any spots open"),
        ("Grouping", "could i get an invite"),
        ("Grouping", "group anyone?"),
        ("Grinding", "where is good xp at 24"),
        ("Grinding", "good camp for a 30 paladin?"),
        ("Grinding", "best xp spot for 40"),
        ("Grinding", "any good camps open"),
        ("Grinding", "whats good solo xp at 18"),
        ("Grinding", "is salisbury good for xp at 15"),
        ("Grinding", "camping the tree in lyonesse"),
        ("Grinding", "where to level at 45"),
        ("Grinding", "how is the xp in cornwall"),
        ("Grinding", "anyone know a good spot for a 20"),
        ("Grinding", "these mobs give great xp"),
        ("Grinding", "any camp bonus left anywhere"),
        ("Grinding", "farming pygmy goblins"),
        ("Grinding", "this camp is dead"),
        ("Grinding", "what cons yellow to a 33"),
        ("Grinding", "grinding barrows again"),
        ("Grinding", "good loot from the giants"),
        ("Grinding", "where is the best cash camp"),
        ("Grinding", "how long to 50 from 45"),
        ("Grinding", "slow night for xp"),
        ("Travel", "how do i get to camelot"),
        ("Travel", "where is the stable master"),
        ("Travel", "how do i get to the frontier"),
        ("Travel", "where is the cleric trainer"),
        ("Travel", "how far is lyonesse"),
        ("Travel", "where do i find the bind stone"),
        ("Travel", "where is the bank"),
        ("Travel", "how do i get to darkness falls"),
        ("Travel", "where is the smith in camelot"),
        ("Travel", "can i port to the frontier"),
        ("Travel", "where is prydwen bridge"),
        ("Travel", "how do i get out of the city"),
        ("Travel", "is avalon marsh far"),
        ("Travel", "how do you get to the battleground"),
        ("Travel", "where is the guild registrar"),
        ("Travel", "which way is north"),
        ("Travel", "how do i get to tomb of mithra"),
        ("Travel", "where is ludlow"),
        ("Travel", "where can i repair"),
        ("Travel", "is there a boat to avalon"),
        ("Dungeon", "anyone doing barrows"),
        ("Dungeon", "is keltoi good at 22"),
        ("Dungeon", "going into mithra"),
        ("Dungeon", "whats in the catacombs of cardova"),
        ("Dungeon", "is df open"),
        ("Dungeon", "barrows group?"),
        ("Dungeon", "what level for tepoks"),
        ("Dungeon", "tomb of mithra is full of undead"),
        ("Dungeon", "who holds df"),
        ("Dungeon", "stuck at the bottom of barrows"),
        ("Dungeon", "loot in barrows is nice"),
        ("Dungeon", "df is ours"),
        ("Dungeon", "dungeon crawl tonight?"),
        ("Dungeon", "what drops in tepoks"),
        ("Dungeon", "anyone killed the legion"),
        ("Dungeon", "corpse run in mithra, ugh"),
        ("Dungeon", "tepoks is camped"),
        ("Dungeon", "are there imps in df"),
        ("Dungeon", "whats the respawn on the barrows boss"),
        ("Dungeon", "anyone seen the prince in df"),
        ("RvR", "anyone in emain"),
        ("RvR", "how is the frontier"),
        ("RvR", "any keeps to take"),
        ("RvR", "lets take bled"),
        ("RvR", "relic raid tonight?"),
        ("RvR", "anyone for thidranki"),
        ("RvR", "we lost beno again"),
        ("RvR", "the zerg is at amg"),
        ("RvR", "who is defending"),
        ("RvR", "forming for keep take"),
        ("RvR", "emain is dead"),
        ("RvR", "we need to retake the strength relic"),
        ("RvR", "whats the bonus for owning keeps"),
        ("RvR", "caledonia anyone"),
        ("RvR", "hibs are zerging"),
        ("RvR", "i got ganked at the mile gate"),
        ("RvR", "keep doors are down"),
        ("RvR", "what rr are you"),
        ("RvR", "where is the fight"),
        ("RvR", "they have 3 relics now"),
        ("Trading", "wts cloak"),
        ("Trading", "wtb chain sleeves"),
        ("Trading", "anyone selling arrows"),
        ("Trading", "pc on a 99% hauberk"),
        ("Trading", "selling 16 dps staff"),
        ("Trading", "need a tailor"),
        ("Trading", "wts stack of leather"),
        ("Trading", "looking for a fletcher"),
        ("Trading", "wtb sc for my set"),
        ("Trading", "who sells dyes"),
        ("Trading", "wtt my axe for a sword"),
        ("Trading", "can someone craft me a bow"),
        ("Trading", "what do you want for it"),
        ("Trading", "is 5g fair"),
        ("Trading", "selling drops cheap"),
        ("Trading", "anyone have a spare shield"),
        ("Trading", "how much is a horse"),
        ("Trading", "where can i sell this"),
        ("Trading", "got any potions for sale"),
        ("Trading", "lf alchemist"),
        ("Help", "can someone help me"),
        ("Help", "how do i train"),
        ("Help", "i need help with a quest"),
        ("Help", "how does spec work"),
        ("Help", "how do i bind"),
        ("Help", "can anyone explain realm abilities"),
        ("Help", "what is a bubble"),
        ("Help", "help"),
        ("Help", "what do i do with these hides"),
        ("Help", "can i respec"),
        ("Help", "whats the command for anon"),
        ("Help", "when do i get my next ability"),
        ("Help", "what is power regen"),
        ("Help", "any tips for a new player"),
        ("Help", "what does quality do"),
        ("Help", "how do i sell stuff"),
        ("Help", "i dont understand resists"),
        ("Help", "what happens when i die"),
        ("Help", "when can i use a horse"),
        ("Help", "do i lose xp when i die"),
        ("Banter", "this lag is killing me"),
        ("Banter", "lol the guards never help"),
        ("Banter", "my troll keeps getting stuck in doors"),
        ("Banter", "same"),
        ("Banter", "ugh"),
        ("Banter", "that pull went well"),
        ("Banter", "what a night"),
        ("Banter", "these goblins hate me"),
        ("Banter", "my keyboard is sticky"),
        ("Banter", "so tired"),
        ("Banter", "phew"),
        ("Banter", "we rock"),
        ("Banter", "slow night"),
        ("Banter", "oh well"),
        ("Banter", "of course it resists"),
        ("Banter", "typical"),
        ("Banter", "mythic hates casters"),
        ("Banter", "ever notice the fish never drop fish"),
        ("Banter", "well that was embarrassing"),
        ("Banter", "nothing but grey loot"),
        ("Abuse", "you suck"),
        ("Abuse", "ur an idiot"),
        ("Abuse", "you are all noobs"),
        ("Abuse", "stfu"),
        ("Abuse", "you are useless"),
        ("Abuse", "l2p"),
        ("Abuse", "what a loser"),
        ("Abuse", "you people are pathetic"),
        ("Abuse", "learn to play"),
        ("Abuse", "u r terrible"),
        ("Abuse", "ur so bad"),
        ("Abuse", "nice pull genius"),
        ("Abuse", "dumbass"),
        ("Abuse", "you are a waste of space"),
        ("Abuse", "you are the worst group ive seen"),
        ("Abuse", "u dumb?"),
        ("Abuse", "stop being a jerk"),
        ("Abuse", "you are trash"),
        ("Abuse", "quit ksing me"),
        ("Abuse", "everyone here is awful"),
        ("NotAbuse", "sorry guys dumb question"),
        ("NotAbuse", "can i ask you a stupid question"),
        ("NotAbuse", "ur not stupid, it happens"),
        ("NotAbuse", "you are not a loser lol"),
        ("NotAbuse", "that was dumb of me, sorry guys"),
        ("NotAbuse", "dont listen to him ur not dumb"),
        ("NotAbuse", "do you guys think scouts are useless"),
        ("NotAbuse", "everyone says clerics are worthless solo"),
        ("NotAbuse", "stupid mob resisted all my mez"),
        ("NotAbuse", "this stupid quest is bugged, anyone know it"),
        ("NotAbuse", "is it dumb to spec parry on a paladin"),
        ("NotAbuse", "ugly armor but good stats, u like it?"),
        ("NotAbuse", "people say im a loser for playing this much"),
        ("NotAbuse", "you ever do something really dumb in a keep"),
        ("NotAbuse", "my guild leader is an idiot but i like him"),
        ("NotAbuse", "that idiot pulled the whole room, not you"),
        ("NotAbuse", "your pet is ugly but effective"),
        ("NotAbuse", "the loser of the duel buys the drinks guys"),
        ("NotAbuse", "im useless without a healer, you got one?"),
        ("NotAbuse", "stupid lag, did you all freeze too"),
        ("NotGreetFarewell", "hey can someone rez me"),
        ("NotGreetFarewell", "hi, where is the trainer"),
        ("NotGreetFarewell", "hey whats the best xp at 20"),
        ("NotGreetFarewell", "yo anyone selling a shield"),
        ("NotGreetFarewell", "hey is anyone in barrows"),
        ("NotGreetFarewell", "evening, any groups going"),
        ("NotGreetFarewell", "morning, who has the relics"),
        ("NotGreetFarewell", "later we should do df"),
        ("NotGreetFarewell", "see you in emain"),
        ("NotGreetFarewell", "ill be back later tonight"),
        ("NotGreetFarewell", "back to camp"),
        ("NotGreetFarewell", "back in barrows again"),
        ("NotGreetFarewell", "dont go yet"),
        ("NotGreetFarewell", "good night for a keep raid"),
        ("NotGreetFarewell", "its late but one more pull"),
        ("NotGreetFarewell", "nobody say gn yet"),
        ("NotGreetFarewell", "bed can wait"),
        ("NotGreetFarewell", "out of arrows"),
        ("NotGreetFarewell", "gone linkdead twice"),
        ("NotGreetFarewell", "is he gone?"),
        ("NotThanksEtc", "no thanks"),
        ("NotThanksEtc", "thanks for nothing"),
        ("NotThanksEtc", "ty but no"),
        ("NotThanksEtc", "gee thanks"),
        ("NotThanksEtc", "thx mythic"),
        ("NotThanksEtc", "no need to thank me"),
        ("NotThanksEtc", "did he say thanks?"),
        ("NotThanksEtc", "ding dong"),
        ("NotThanksEtc", "when do you ding"),
        ("NotThanksEtc", "almost ding"),
        ("NotThanksEtc", "he dinged, not me"),
        ("NotThanksEtc", "gz to him i guess"),
        ("NotThanksEtc", "dont say gz yet"),
        ("NotThanksEtc", "not funny"),
        ("NotThanksEtc", "lol jk"),
        ("NotThanksEtc", "not back yet"),
        ("NotThanksEtc", "he is afk"),
        ("NotThanksEtc", "is the healer afk?"),
        ("NotThanksEtc", "dont go afk"),
        ("NotThanksEtc", "who is afk"),
        ("NotRezAlarm", "rez sickness sucks"),
        ("NotRezAlarm", "i hate rez sickness"),
        ("NotRezAlarm", "what level do clerics get rez"),
        ("NotRezAlarm", "got rezzed twice tonight"),
        ("NotRezAlarm", "no rez needed"),
        ("NotRezAlarm", "who rezzed me? ty"),
        ("NotRezAlarm", "heat res gear"),
        ("NotRezAlarm", "is the inc over"),
        ("NotRezAlarm", "false alarm"),
        ("NotRezAlarm", "any inc tonight?"),
        ("NotRezAlarm", "decent inc from that camp"),
        ("NotRezAlarm", "was that an add or the pull"),
        ("NotRezAlarm", "dont run"),
        ("NotRezAlarm", "run speed is slow"),
        ("NotRezAlarm", "the train station lol"),
        ("NotRezAlarm", "mids are quiet tonight"),
        ("NotRezAlarm", "were there albs at the gate earlier"),
        ("NotRezAlarm", "did they take beno"),
        ("NotRezAlarm", "anyone seen mids today"),
        ("NotRezAlarm", "i miss the old zerg"),
        ("NotGroupTradeEtc", "my group just wiped"),
        ("NotGroupTradeEtc", "love this group"),
        ("NotGroupTradeEtc", "the group left without me"),
        ("NotGroupTradeEtc", "not lfg, just chatting"),
        ("NotGroupTradeEtc", "our party is full sorry"),
        ("NotGroupTradeEtc", "groups are hard to find at 30"),
        ("NotGroupTradeEtc", "i hate grouping with strangers"),
        ("NotGroupTradeEtc", "i dont want to sell it"),
        ("NotGroupTradeEtc", "i sold it already"),
        ("NotGroupTradeEtc", "never buy from him"),
        ("NotGroupTradeEtc", "im broke"),
        ("NotGroupTradeEtc", "merchant wont take it"),
        ("NotGroupTradeEtc", "where we headed?"),
        ("NotGroupTradeEtc", "where are we headed"),
        ("NotGroupTradeEtc", "where to?"),
        ("NotGroupTradeEtc", "what next"),
        ("NotGroupTradeEtc", "we pulling or what"),
        ("NotGroupTradeEtc", "ready?"),
        ("NotGroupTradeEtc", "who is pulling"),
        ("NotGroupTradeEtc", "is the healer oom")
    ];

    private static bool ReadRight(string expect, string text)
    {
        Intent got = AutonomousChatIntentModel.Predict(text);
        return Wrong.TryGetValue(expect, out Intent[] wrong) ? !wrong.Contains(got) : got.ToString() == expect;
    }

    [Test]
    public void TheFixedReadSetIsReadRight()
    {
        // Before the fixes: 74.8% of the positive lines and 60.7% of the "not X" lines (this subset). After: 90% and 95%.
        var positive = ReadSet.Where(item => !Wrong.ContainsKey(item.Expect)).ToArray();
        var negative = ReadSet.Where(item => Wrong.ContainsKey(item.Expect)).ToArray();
        double p = positive.Count(item => ReadRight(item.Expect, item.Text)) / (double)positive.Length;
        double n = negative.Count(item => ReadRight(item.Expect, item.Text)) / (double)negative.Length;
        string missed = string.Join("; ", ReadSet.Where(item => !ReadRight(item.Expect, item.Text)).Select(item => item.Expect + ": " + item.Text));
        Assert.That(p, Is.GreaterThanOrEqualTo(0.88), "positive lines read as their intent; missed: " + missed);
        Assert.That(n, Is.GreaterThanOrEqualTo(0.92), "lines kept clear of the wrong intent; missed: " + missed);
    }

    // ---- DESIGN-001: abuse needs a target, and not a negated one ----------------------------------------------------------------

    [TestCase("sorry guys dumb question")]
    [TestCase("can i ask you a stupid question")]
    [TestCase("im so stupid, sorry all")]
    [TestCase("ur not stupid, it happens")]
    [TestCase("you are not a loser lol")]
    [TestCase("dont listen to him ur not dumb")]
    [TestCase("my guild leader is an idiot but i like him")]
    [TestCase("that idiot pulled the whole room, not you")]
    [TestCase("stupid lag, did you all freeze too")]
    [TestCase("dumb luck that we lived guys")]
    [TestCase("everyone says clerics are worthless solo")]
    public void SelfDeprecationNegationAndThirdPartiesAreNotAbuse(string line) =>
        Assert.That(AutonomousChatIntentModel.Predict(line), Is.Not.EqualTo(Intent.Abuse), line);

    [TestCase("you idiot")]
    [TestCase("idiot")]
    [TestCase("ur so stupid")]
    [TestCase("you are all pathetic")]
    [TestCase("u suck")]
    [TestCase("stfu noob")]
    [TestCase("what a loser")]
    [TestCase("everyone here is awful")]
    [TestCase("you are a waste of space")]
    public void AimedInsultsAreStillAbuse(string line) => Assert.That(AutonomousChatIntentModel.Predict(line), Is.EqualTo(Intent.Abuse), line);

    // ---- DESIGN-011, CORPUS-C-017: "stock" is game talk -------------------------------------------------------------------------

    [TestCase("what do you have in stock", false)]
    [TestCase("stock up on arrows before we go", false)]
    [TestCase("watched the football game", false)]
    [TestCase("taxes are due, ugh", false)]
    [TestCase("the stock market crashed again", true)]
    [TestCase("taxes and the government lol", true)]
    [TestCase("did you see the president on tv", true)]
    public void RealWorldTalkNeedsARealWorldWord(string line, bool outOfWorld) =>
        Assert.That(AutonomousChatIntentModel.Predict(line) == Intent.IgnoreOutOfWorld, Is.EqualTo(outOfWorld), line);

    // ---- DESIGN-002: a question without "?" ------------------------------------------------------------------------------------

    [TestCase("where is the cleric trainer")]
    [TestCase("how do i get to camelot")]
    [TestCase("is df open")]
    [TestCase("does anyone know who has the str relic")]
    public void AQuestionWithoutItsMarkIsAQuestion(string line)
    {
        Assert.That(AutonomousBotChatCoordinator.NeedsAnswer(line), Is.True, "the cooldown must not drop it");
        Assert.That(AutonomousChatThreads.ReadsAsAnswer(line, AutonomousChatIntentModel.Predict(line)), Is.False, "not an answer to a bot");
    }

    // ---- DESIGN-005, 041: the group's own questions ----------------------------------------------------------------------------

    [TestCase("where we headed?", "ask_next")]
    [TestCase("where are we headed", "ask_next")]
    [TestCase("where to?", "ask_next")]
    [TestCase("what next", "ask_next")]
    [TestCase("whats the plan", "ask_plans")]
    [TestCase("ready?", "ask_ready")]
    [TestCase("everyone ready", "ask_ready")]
    [TestCase("we pulling or what", "ask_ready")]
    [TestCase("is the healer oom", "ask_ready")]
    [TestCase("how much power you got", "ask_ready")]
    [TestCase("who is pulling", "ask_who_pulls")]
    public void GroupQuestionsHaveTheirShape(string line, string shape)
    {
        Assert.That(Short.QuestionShape(line), Is.EqualTo(shape), line);
        Assert.That(AutonomousBotChatCoordinator.NeedsAnswer(line), Is.True);
    }

    [Test]
    public void ReadyIsNoCheckOnTheLastBotLineAndPowerIsNoPrice()
    {
        Assert.That(AutonomousChatThreads.FollowUpWord("ready?"), Is.Null);
        Assert.That(AutonomousChatIntentModel.Predict("how much power you got"), Is.Not.EqualTo(Intent.Trading));
    }

    private static AutonomousChatContext.Facts Facts(string channel, int health = 100, int power = 100, bool inRvr = false, bool comes = false) =>
        new("Brannoc", "Wynn", channel, true, false, inRvr, 45, null, "Jordheim", health, power) { TryCome = (_, _) => comes };

    private static (string Route, string Text)? Context(string line, AutonomousChatContext.Facts facts, int seed) =>
        AutonomousChatContext.Reply(line, AutonomousChatIntentModel.Predict(line), Short.QuestionShape(line), facts, new Random(seed));

    [Test]
    public void ReadyIsAnsweredFromRealPowerAndHealth()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            Assert.That(Context("ready?", Facts("group", power: 20), seed)?.Text, Does.Contain("pow").Or.Contain("oom").Or.Contain("med"), "low power says so");
            Assert.That(Context("ready?", Facts("group", health: 30), seed)?.Text, Does.Contain("hp"), "low health says so");
            Assert.That(Context("everyone ready", Facts("group"), seed)?.Text, Does.Not.Contain("pow, sec").And.Not.Contain("hp"), "full: ready");
            Assert.That(Context("ready?", Facts("group"), seed)?.Route, Is.EqualTo("context ready"));
        }
    }

    // ---- DESIGN-006, CORPUS-C-018: a promise to come only through the gate ------------------------------------------------------

    private static readonly string[] Promises = ["omw", "on my way", "coming", "ill be there", "count me in", "heals coming", "healing", "on it", "otw"];

    private static bool Promised(string text) =>
        text != null && Promises.Any(promise => (" " + text + " ").Contains(" " + promise, StringComparison.Ordinal));

    [Test]
    public void NoBotSaysItIsComingUnlessThePromiseGateSendsIt()
    {
        string[] calls = ["inc 3 albs at amg", "zerg at mmg, 40+ hibs", "free rps at mmg, mids are afk", "help please, this mob is killing me",
            "relic raid at 9, meet at portal keep", "they took our relic", "emain is hot tonight", "corpse run in barrows, help"];
        for (int seed = 0; seed < 40; seed++)
            foreach (string call in calls)
                foreach (string channel in new[] { "faction", "say" })
                    Assert.That(Promised(Context(call, Facts(channel), seed)?.Text), Is.False, $"{channel}: {call}");
        Assert.That(Enumerable.Range(0, 40).Any(seed => Promised(Context("inc 3 albs at amg", Facts("faction", comes: true), seed)?.Text)), Is.True,
            "with the gate open, the old omw lines are said");
        Assert.That(ChatPromises.TryPromiseCome(null, "amg", ChatPromiseReason.Alarm), Is.False, "the stub never promises");
    }

    [Test]
    public void TheReplyBudgetsShortAnswersPromiseNothingEither()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            Assert.That(Promised(AutonomousChatReplyBudget.AlarmReaction("inc at the bridge", new Random(seed))), Is.False);
            var healer = new AutonomousChatReplyBudget.Moment("need heals!!", "faction", false, Intent.Unknown, "Druid", true, true, false);
            Assert.That(Promised(AutonomousChatReplyBudget.RequestAnswer(healer, " need heals ", new Random(seed))), Is.False);
            Assert.That(Promised(AutonomousChatReplyBudget.RequestAnswer(healer, " rez pls ", new Random(seed))), Is.False);
        }
        Assert.That(AutonomousChatReplyBudget.BusyAck(false, new Random(1)), Does.Not.Contain("fighting"), "casting is no fight");
    }

    [Test]
    public void TheRealmNewsCheerIsPeriodChat() =>
        Assert.That(Enumerable.Range(0, 60).Select(seed => Context("DF is ours again", Facts("faction"), seed)?.Text), Has.None.Contains("\\o/"));

    // ---- DESIGN-009: calls -----------------------------------------------------------------------------------------------------

    [TestCase("albs inc from the south")]
    [TestCase("inc from behind")]
    [TestCase("they are hitting bold")]
    [TestCase("4 mids on the bridge")]
    [TestCase("hibs everywhere at dc")]
    [TestCase("mids taking berk")]
    [TestCase("rams on the door at crim")]
    [TestCase("sb just jumped me at the dock")]
    public void PlainCallsAreAlarms(string line) => Assert.That(AutonomousChatIntentModel.Predict(line), Is.EqualTo(Intent.Alarm), line);

    [TestCase("decent inc from that camp")]
    [TestCase("made good inc today")]
    public void IncomeIsNoCall(string line) => Assert.That(AutonomousChatIntentModel.Predict(line), Is.Not.EqualTo(Intent.Alarm), line);

    // ---- DESIGN-010: the social word talked about ------------------------------------------------------------------------------

    [TestCase("3 bubs from ding", Intent.Ding)]
    [TestCase("almost ding", Intent.Ding)]
    [TestCase("no ding yet", Intent.Ding)]
    [TestCase("when do you ding", Intent.Ding)]
    [TestCase("he dinged, not me", Intent.Ding)]
    [TestCase("no gz for me?", Intent.Congrats)]
    [TestCase("dont say gz yet", Intent.Congrats)]
    [TestCase("ty but no", Intent.Thanks)]
    [TestCase("dont thank me yet", Intent.Thanks)]
    [TestCase("did he say thanks?", Intent.Thanks)]
    [TestCase("he is afk", Intent.Away)]
    [TestCase("dont go afk", Intent.Away)]
    [TestCase("who is afk", Intent.Away)]
    [TestCase("dont rez me", Intent.Rez)]
    [TestCase("no rez needed", Intent.Rez)]
    [TestCase("what level do clerics get rez", Intent.Rez)]
    [TestCase("no inc", Intent.Alarm)]
    [TestCase("is the inc over", Intent.Alarm)]
    [TestCase("any inc tonight?", Intent.Alarm)]
    [TestCase("where is the inc", Intent.Alarm)]
    [TestCase("im out of power", Intent.Farewell)]
    [TestCase("not going to bed yet", Intent.Farewell)]
    [TestCase("good night for a keep raid", Intent.Farewell)]
    [TestCase("bye bye strength relic", Intent.Farewell)]
    public void ASocialWordTalkedAboutIsNotTheAct(string line, Intent not) =>
        Assert.That(AutonomousChatIntentModel.Predict(line), Is.Not.EqualTo(not), line);

    [TestCase("ding!", Intent.Ding)]
    [TestCase("gz", Intent.Congrats)]
    [TestCase("ty all", Intent.Thanks)]
    [TestCase("brb", Intent.Away)]
    [TestCase("rez pls", Intent.Rez)]
    [TestCase("can someone rez me", Intent.Rez)]
    [TestCase("inc!", Intent.Alarm)]
    [TestCase("im out, ty all", Intent.Farewell)]
    [TestCase("back from afk", Intent.Back)]
    // DESIGN-047: the missing goodbye and return words
    [TestCase("ttyl", Intent.Farewell)]
    [TestCase("bbl", Intent.Farewell)]
    [TestCase("signing off", Intent.Farewell)]
    [TestCase("see you all tomorrow", Intent.Farewell)]
    [TestCase("see ya", Intent.Farewell)]
    [TestCase("bak", Intent.Back)]
    [TestCase("back in 5", Intent.Away)]
    public void TheActItselfIsStillRead(string line, Intent intent) => Assert.That(AutonomousChatIntentModel.Predict(line), Is.EqualTo(intent), line);

    // ---- CORPUS-C-053: alarm replies don't ask what the alarm said ---------------------------------------------------------------

    [Test]
    public void AnAlarmIsNotAskedWhatItSaid()
    {
        Assert.That(Short.ReplyFits("inc 8 mids at bled", "where?"), Is.False);
        Assert.That(Short.ReplyFits("inc 8 mids at bled", "how many?"), Is.False);
        Assert.That(Short.ReplyFits("inc 2 sbs and a healer at the bridge", "what classes?"), Is.False);
        Assert.That(Short.ReplyFits("inc!", "where?"), Is.True, "a bare inc is asked where");
    }

    // ---- CORPUS-C-001, 004, 007, 008: which set answers and closes ---------------------------------------------------------------

    private static ChatThreadLog.Said Asked(string text, string invites, string to = null, long at = 0) =>
        new("faction:Albion", 7, "Aldric", text, invites, at) { To = to };

    [Test]
    public void APersonalQuestionClosesPersonally()
    {
        Assert.That(AutonomousChatThreads.ClosingSets(Asked("hey Wynn, dinged yet?", "ask_personal"), "42 now")[0], Is.EqualTo("ask_personal"));
        Assert.That(AutonomousChatThreads.ClosingSets(Asked("where have you been hiding?", "ask_where"), "work, sorry")[0], Is.EqualTo("ask_personal"));
        Assert.That(AutonomousChatThreads.ClosingSets(Asked("where is the bone camp?", "ask_where"), "past the bridge")[0], Is.EqualTo("ask_where"));
    }

    [Test]
    public void ABotsClosingLooksAtTheAnswer()
    {
        Assert.That(AutonomousBotChatCoordinator.BotClosingSets("ask_where", "no idea sorry"), Is.EqualTo(new[] { "ask_unknown" }));
        Assert.That(AutonomousBotChatCoordinator.BotClosingSets("ask_where", "past the bridge"), Is.EqualTo(new[] { "ask_where", "ask" }));
    }

    [Test]
    public void TheAnswerChainReadsTheOpener()
    {
        Assert.That(AutonomousBotChatCoordinator.AnswerChainFor("pc thrustbender mantle?", "trade"), Is.EqualTo(new[] { "ask_price" }), "a price check is no sale");
        Assert.That(AutonomousBotChatCoordinator.AnswerChainFor("anyone on?", "ask_anyone")[0], Is.EqualTo("ask_on"));
        Assert.That(AutonomousBotChatCoordinator.AnswerChainFor("anyone else level their infil solo or am i doing it wrong?", "ask_anyone"),
            Is.EqualTo(new[] { "ask_opinion", "ask" }));
        Assert.That(AutonomousBotChatCoordinator.AnswerChainFor("two handed or sword and board?", "ask")[0], Is.EqualTo("ask_choice"));
    }

    [Test]
    public void AWrittenQuestionIsAnswered()
    {
        var question = new VC.Line("q", "rvr", "general", [], "*", [], 1, 50, [], "", "chat", 1, "frontier later?") { Invites = "ask" };
        var remark = new VC.Line("r", "ambient", "general", [], "*", [], 1, 50, [], "", "chat", 1, "quiet night") { Invites = "chat" };
        Assert.That(AutonomousBotChatCoordinator.WrittenQuestionInvites(question), Is.EqualTo("ask"));
        Assert.That(AutonomousBotChatCoordinator.WrittenQuestionInvites(remark), Is.Null);
    }

    // ---- DESIGN-008, 042, LOG-040: threads in a busy channel ---------------------------------------------------------------------

    [Test]
    public void AnAnswerFindsTheQuestionPastTwoUnrelatedLines()
    {
        var log = new ChatThreadLog();
        log.Record("faction:Albion", 1, "Aldric", "anyone know where the bone camp is?", "ask_where", 1_000);
        log.Record("faction:Albion", 2, "Brynn", "lol that ninja", "chat", 3_000);
        log.Record("faction:Albion", 3, "Cael", "who is camping the fogou named", "ask", 5_000);
        Assert.That(log.OpenQuestion("faction:Albion", 6_000, 75_000)?.BotName, Is.EqualTo("Cael"), "the old reading: the last line");
        Assert.That(log.OpenQuestion("faction:Albion", "Wynn", "the bone camp is past the bridge", 6_000, 75_000)?.BotName, Is.EqualTo("Aldric"),
            "the answer shares words with the question it answers");
        log.Record("faction:Albion", 4, "Dorn", "what level are you?", "ask", 7_000, "Wynn");
        Assert.That(log.OpenQuestion("faction:Albion", "Wynn", "32", 8_000, 75_000)?.BotName, Is.EqualTo("Dorn"), "one said to the player first");
        Assert.That(log.FollowUpTarget("faction:Albion", "Wynn", "why?", 8_000, 90_000)?.BotName, Is.EqualTo("Dorn"));
        Assert.That(log.LinesSince("faction:Albion", 1_000, new[] { 1L }), Is.EqualTo(3));
    }

    [Test]
    public void AnAnswerAfterOtherLinesNamesTheAsker()
    {
        Assert.That(AutonomousBotChatCoordinator.NameTheAsker("back to whoever gave it", "Vanessa", 1), Is.EqualTo("vanessa back to whoever gave it"));
        Assert.That(AutonomousBotChatCoordinator.NameTheAsker("back to whoever gave it", "Vanessa", 0), Is.EqualTo("back to whoever gave it"));
    }

    [Test]
    public void ThreeTurnsWithLinesBetween()
    {
        // A bot asks, two lines of other exchanges land, the player answers, then asks "why?": each turn finds its own line.
        var log = new ChatThreadLog();
        log.Record("say:1", 1, "Aldric", "you heading to vendo?", "ask", 0, "Wynn");
        log.Record("say:1", 2, "Brynn", "gz Cael", "chat", 2_000);
        log.Record("say:1", 3, "Cael", "ty ty", "none", 3_000);
        ChatThreadLog.Said q = log.OpenQuestion("say:1", "Wynn", "yeah, want to come?", 4_000, 75_000);
        Assert.That(q?.BotName, Is.EqualTo("Aldric"));
        log.Close(q);
        log.Record("say:1", 1, "Aldric", "cant, low on pots", "chat", 6_000, "Wynn");
        Assert.That(log.OpenQuestion("say:1", "Wynn", "ok", 7_000, 75_000), Is.Null, "the question is closed");
        Assert.That(log.FollowUpTarget("say:1", "Wynn", "why?", 8_000, 90_000)?.Text, Is.EqualTo("cant, low on pots"));
    }

    // ---- DESIGN-003, 018: tells -----------------------------------------------------------------------------------------------

    [Test]
    public void AQuickSecondTellIsQueuedNotDropped()
    {
        Assert.That(AutonomousBotChatCoordinator.QueuedTellDelay(10_000, 11_000), Is.EqualTo(2_000));
        Assert.That(AutonomousBotChatCoordinator.QueuedTellDelay(10_000, 20_000), Is.Zero);
        Assert.That(AutonomousBotChatCoordinator.TellMaxWaitMs, Is.GreaterThanOrEqualTo(90_000));
        Assert.That(AutonomousBotChatCoordinator.DirectTypingCapMs, Is.LessThanOrEqualTo(12_000));
    }

    // ---- LOG-002: answers spread over the regulars -------------------------------------------------------------------------------

    private sealed class Reg { public long Id; }

    [Test]
    public void OneRegularDoesNotAnswerEverything()
    {
        var regulars = Enumerable.Range(1, 8).Select(i => new Reg { Id = i }).ToArray();
        var last = new Dictionary<long, long>();
        var random = new Random(5);
        var counts = new Dictionary<long, int>();
        for (long now = 0; now < 3_600_000; now += 20_000)
        {
            Reg pick = AutonomousBotChatCoordinator.PickRested(regulars, reg => reg.Id, last, now, random);
            last[pick.Id] = now;
            counts[pick.Id] = counts.GetValueOrDefault(pick.Id) + 1;
        }
        Assert.That(counts.Values.Max() / (double)counts.Values.Sum(), Is.LessThan(0.25), "no regular takes a quarter of the answers");
        Assert.That(counts.Count, Is.EqualTo(8));
    }

    // ---- DESIGN-017, 034, TEXT-043: codes and /lswhy -------------------------------------------------------------------------------

    [Test]
    public void AnEventsLineIsExemptNotHeld()
    {
        Assert.That(AutonomousChatBudget.DecideReply(AutonomousChatBudget.ReplyKind.Event, 99, (1, 2), 0.5), Is.EqualTo(AutonomousChatBudget.ReplyVerdict.Exempt));
        Assert.That(AutonomousChatWhy.Codes.Of(AutonomousChatBudget.ReplyVerdict.Exempt, AutonomousChatBudget.ReplyKind.Event), Is.EqualTo("exempt-event"));
        Assert.That(AutonomousBotChatCoordinator.AmbientCode(true), Is.EqualTo("ambient-under-floor"));
    }

    [Test]
    public void APlayerReadsWhyWithoutCodes()
    {
        var decision = new AutonomousChatWhy.Decision(0, 7, "Aldric", eRealm.Albion, "faction", "player Wynn", "said", "shape ask_hunt", 4, "abc123",
            "answers what was asked", "try the barrows", "Wynn") { Code = "exempt-named" };
        string player = AutonomousChatWhy.DescribeForPlayer(decision, 2_000);
        Assert.That(player, Does.Contain("Aldric said").And.Contain("try the barrows"));
        Assert.That(player, Does.Not.Contain("exempt").And.Not.Contain("rule").And.Not.Contain("score").And.Not.Contain("abc123"));
        Assert.That(AutonomousChatWhy.Describe(decision, 2_000), Does.Contain("exempt-named"), "a GM still sees the code");
        var dropped = new AutonomousChatWhy.Decision(0, 0, null, eRealm.Albion, "faction", "player Wynn", "silent", "dropped cooldown", 0, null,
            "bots answered you a moment ago", null, "Wynn");
        Assert.That(AutonomousChatWhy.DescribeForPlayer(dropped, 0), Does.Contain("nobody answered"));
    }

    [Test]
    public void EveryExitOfThePlayerChatEntryPointsRecordsWhy()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        string code = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous", "AutonomousBotChatCoordinator.cs"));
        foreach (string method in new[] { "public static void OnPlayerFactionChat(", "public static void OnPlayerLocalChat(" })
        {
            int start = code.IndexOf(method, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThan(0), method);
            int open = code.IndexOf('{', start), depth = 0, end = open;
            for (; end < code.Length; end++)
                if (code[end] == '{') depth++;
                else if (code[end] == '}' && --depth == 0) break;
            string[] lines = code[open..end].Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "return;")
                    continue;
                string before = string.Join("\n", lines.Skip(Math.Max(0, i - 4)).Take(Math.Min(4, i)));
                bool records = before.Contains("Dropped(") || before.Contains("RecordSilence(") || before.Contains("Screened(") ||
                               before.Contains("TryThreadReply(") || before.Contains("IsNullOrWhiteSpace(message)");
                Assert.That(records, Is.True, method + " returns without a record after: " + before);
            }
        }
    }

    // ---- CORPUS-C-003, LOG-027: spec questions ---------------------------------------------------------------------------------

    [TestCase("anyone know a good paladin spec?", true)]
    [TestCase("how should i spec my zerk", true)]
    [TestCase("what do i put points in on a thane?", true)]
    [TestCase("paladins have so many chants", false)]
    [TestCase("my spec is max chants", false)]
    public void ASpecQuestionIsRead(string line, bool spec) => Assert.That(Short.AsksSpec(line), Is.EqualTo(spec), line);

    [Test]
    public void ASpecQuestionHasARealAnswer() =>
        Assert.That(AutonomousChatFieldTalk.SpecAdvice("paladin"), Has.Some.Contains("chants"));
}
