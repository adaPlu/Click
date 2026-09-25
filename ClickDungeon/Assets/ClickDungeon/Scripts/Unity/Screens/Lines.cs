using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Unity.Screens
{
    public enum Expression { Neutral, Happy, Confident, Worried, Shocked, Angry, Victorious, Defeated }

    /// <summary>Player-facing words: the hero's quips, the turn log, and tile/intent explanations.</summary>
    public static class Lines
    {
        static readonly System.Random Rng = new System.Random();

        static string Pick(params string[] options) => options[Rng.Next(options.Length)];

        public static string Face(Expression e)
        {
            switch (e)
            {
                case Expression.Happy: return ":D";
                case Expression.Confident: return ";)";
                case Expression.Worried: return ":/";
                case Expression.Shocked: return ":O";
                case Expression.Angry: return ">:(";
                case Expression.Victorious: return "\\o/";
                case Expression.Defeated: return "x_x";
                default: return ":)";
            }
        }

        /// <summary>Each boss greets the hero in its own voice (D-062): the Bat Roost used to open with Blobert's line.</summary>
        public static string FloorStart(FloorState floor) => floor.IsBossFloor
            ? floor.Enemies.Select(e => BossGreeting(e.DefId)).FirstOrDefault(g => g != null) ?? BossGreeting("lord_blobert")
            : Pick("Onward! Probably.", "Deeper we go. Bravely-ish.", "Smells like adventure. And goblin.", "Every tile tells a story. Hopefully not a sad one.");

        static string BossGreeting(string bossId)
        {
            switch (bossId)
            {
                case "goblin_brute_king": return "GOBLIN BRUTE KING: \"Bigger crown. More lunch. You're the lunch.\"";
                case "bat_swarm_leader": return "BAT SWARM LEADER: \"You're never just fighting one of us.\"";
                case "theater_curtain_demon": return "CURTAIN DEMON: \"Places, everyone! The show must go on...\"";
                case "lord_blobert": return "LORD BLOBERT: \"You dare challenge the most magnificent blob in all the dungeons?\"";
                default: return null;
            }
        }

        /// <summary>
        /// The playing hero's name, for the lines spoken in their voice. These used to name Sir Clickington outright, so a
        /// Dawnward run ended "Sir Clickington has fallen" — and with the mascot retired from play (D-057), so would every
        /// run. Falls back to a plain word rather than throwing if the run or its hero is somehow missing.
        /// </summary>
        public static string HeroName(RunState run, ContentCatalog catalog) =>
            run?.Hero != null && catalog != null && catalog.HeroIdentities.TryGetValue(run.Hero.IdentityId ?? "", out var identity)
                ? identity.DisplayName
                : "The hero";

        public static string SourceName(string id, ContentCatalog catalog)
        {
            if (id == "spikes") return "spikes";
            if (id == "bomb") return "a bomb";
            if (id == "slash") return "your slash";
            return catalog.HasEnemy(id) ? catalog.Enemy(id).DisplayName : id;
        }

        /// <summary>The boss this floor is sealed by: each act ends in its own (D-062).</summary>
        public static string BossName(RunState run, ContentCatalog catalog)
        {
            var boss = run.Floor.Enemies.Find(e => catalog.HasEnemy(e.DefId) && catalog.Enemy(e.DefId).IsBoss);
            string id = boss?.DefId ?? catalog.ProfileFor(run.Floor.FloorIndex)?.BossId;
            return id != null && catalog.HasEnemy(id) ? catalog.Enemy(id).DisplayName : "the boss";
        }

        public static string EnemyName(RunState run, int actorId, string fallbackId, ContentCatalog catalog)
        {
            var enemy = run.Floor.EnemyById(actorId);
            var id = enemy != null ? enemy.DefId : fallbackId;
            return id != null && catalog.HasEnemy(id) ? catalog.Enemy(id).DisplayName : "Enemy";
        }

        /// <summary>A reaction line with priority (higher wins) for the most important event of a turn.</summary>
        public static int React(GameEvent e, RunState run, ContentCatalog catalog, out string line, out Expression face)
        {
            line = null;
            face = Expression.Neutral;
            switch (e.Kind)
            {
                case GameEventKind.RunLost:
                    line = "Tell my horse... wait. I don't have a horse.";
                    face = Expression.Defeated;
                    return 100;
                case GameEventKind.RunWon:
                    line = "Victory! Snacks for everyone!";
                    face = Expression.Victorious;
                    return 99;
                case GameEventKind.ChestOpened:
                    line = "Treasure! Hold me back!";
                    face = Expression.Happy;
                    return 60;
                case GameEventKind.HeroDamaged:
                    if (run.Hero.Hp <= 3)
                    {
                        line = "I'm fine. This is fine.";
                        face = Expression.Shocked;
                        return 70;
                    }
                    // A blow worth four hearts is not a scratch, and the face that answers it is not a worried one (D-065).
                    if (e.Amount >= 4)
                    {
                        line = Pick("Right. That does it.", "Now I'm cross.", "You will regret that.");
                        face = Expression.Angry;
                        return 52;
                    }
                    line = e.Source == "spikes" ? "Pointy. Noted." : Pick("Ow. Totally meant that.", "That's going to bruise.", "'Tis but a scratch!");
                    face = Expression.Worried;
                    return 50;
                case GameEventKind.HeroBlocked:
                    line = "Ha! The Shield of Clickington!";
                    face = Expression.Confident;
                    return 55;
                case GameEventKind.EnemyWoke:
                    line = e.Source == "fire_imp" ? "Is it warm in here, or is that an imp?"
                        : e.Source == "crowned_slime" ? "That slime is wearing a crown. Rude."
                        : e.Source == "skeleton" ? "A skeleton. Of course there's a skeleton."
                        : e.Source == "armored_boar" ? "Why is that boar wearing armour?!"
                        : e.Source == "goblin_bomber" ? "Is that goblin holding a... yep."
                        : e.Source == "cave_spider" ? "Eight legs. Why is it always eight legs?"
                        : e.Source == "spooky_spellbook" ? "That book is looking at me."
                        : e.Source == "mimic_chest" ? "THE CHEST HAS TEETH!"
                        : e.Source == "goblin_key_warden" ? "He's got the key! Get him!"
                        : e.Source == "goblin_brute_king" ? "That's a big crown. On a big goblin."
                        : e.Source == "bat_swarm_leader" ? "Bats. So many bats."
                        : e.Source == "theater_curtain_demon" ? "Oh no. It's a musical."
                        : "Oh. Hello there.";
                    face = Expression.Shocked;
                    return 45;
                case GameEventKind.EnemyCollapsed:
                    line = "It's... still twitching.";
                    face = Expression.Worried;
                    return 46;
                case GameEventKind.EnemyReassembled:
                    line = "Bones don't quit, apparently.";
                    face = Expression.Shocked;
                    return 47;
                case GameEventKind.EnemyCharged:
                    line = "Big pig! BIG PIG!";
                    face = Expression.Shocked;
                    return 47;
                case GameEventKind.BombThrown:
                    line = "Incoming!";
                    face = Expression.Worried;
                    return 46;
                case GameEventKind.HeroWebbed:
                    line = "Sticky! So sticky!";
                    face = Expression.Worried;
                    return 47;
                case GameEventKind.HeroDodged:
                    line = "Missed me!";
                    face = Expression.Confident;
                    return 47;
                case GameEventKind.EnemyKnockedBack:
                    line = "And stay back!";
                    face = Expression.Confident;
                    return 44;
                case GameEventKind.KeyDropped:
                    line = "Finders keepers!";
                    face = Expression.Confident;
                    return 46;
                case GameEventKind.BossEnraged:
                    line = "Uh oh. Now he's angry.";
                    face = Expression.Shocked;
                    return 48;
                case GameEventKind.BossVanished:
                    line = "Where did it go?!";
                    face = Expression.Shocked;
                    return 46;
                case GameEventKind.BossDeflated:
                    line = "He's deflating! Now's my chance!";
                    face = Expression.Angry;
                    return 48;
                case GameEventKind.BossPuffed:
                    line = "Too puffy to hit. Keep moving!";
                    face = Expression.Worried;
                    return 47;
                case GameEventKind.KeyCollected:
                    line = "Shiny AND important!";
                    face = Expression.Happy;
                    return 40;
                case GameEventKind.BombArmed:
                    line = "Is that... ticking?";
                    face = Expression.Shocked;
                    return 42;
                case GameEventKind.ExitUnlocked:
                    line = "Click. Open.";
                    face = Expression.Confident;
                    return 35;
                case GameEventKind.VaultEntered:
                    line = "Treasure! Probably guarded. Definitely treasure.";
                    face = Expression.Confident;
                    return 44;
                case GameEventKind.DoorsOpened:
                    line = "Something heavy just swung open.";
                    face = Expression.Shocked;
                    return 38;
                case GameEventKind.Teleported:
                    line = "Every atom, accounted for.";
                    face = Expression.Shocked;
                    return 36;
                case GameEventKind.LavaBurned:
                    line = "Hot! Hot! Hot!";
                    face = Expression.Worried;
                    return 46;
                case GameEventKind.FellThroughPit:
                    line = "Shortcut! Ow. Shortcut.";
                    face = Expression.Shocked;
                    return 48;
                case GameEventKind.EnemyDied:
                    // A boss going down is the moment of the act, not one more kill, so it takes the turn's line (D-065).
                    if (catalog.HasEnemy(e.Source) && catalog.Enemy(e.Source).IsBoss)
                    {
                        line = $"{SourceName(e.Source, catalog)} is down!";
                        face = Expression.Victorious;
                        // Below the hero being down to their last hearts (70) and above every ordinary event: a boss
                        // falling is the moment of the act, but not on the turn it nearly killed them (REL-80).
                        return 68;
                    }
                    line = $"{HeroName(run, catalog)}: 1. Dungeon: 0.";
                    face = Expression.Confident;
                    return 30;
                case GameEventKind.HeroHealed:
                    line = "Tastes like cherries and courage.";
                    face = Expression.Happy;
                    return 25;
                case GameEventKind.HeroDashed:
                    line = "Whoosh!";
                    face = Expression.Confident;
                    return 10;
            }
            return 0;
        }

        /// <summary>Plain-language turn log so every hit can be explained (Pillar B). Null = not logged.</summary>
        public static string Describe(GameEvent e, RunState run, ContentCatalog catalog)
        {
            switch (e.Kind)
            {
                case GameEventKind.HeroDamaged:
                    return $"<color=#FF6B5E>You took {e.Amount}</color> from {SourceName(e.Source, catalog)}.";
                case GameEventKind.HeroBlocked:
                    return $"<color=#F2C94C>Shield blocked {e.Amount}</color> from {SourceName(e.Source, catalog)}.";
                case GameEventKind.HeroHealed:
                    return e.Source == "stairs"
                        ? $"<color=#9FD8A0>Caught your breath on the stairs: healed {e.Amount}.</color>"
                        : $"<color=#9FD8A0>Healed {e.Amount}.</color>";
                case GameEventKind.EnemyMissed:
                    return $"{SourceName(e.Source, catalog)} hit an empty tile.";
                case GameEventKind.EnemyFired:
                    return e.To.InBounds ? null : $"{SourceName(e.Source, catalog)}'s fire hit nothing.";
                case GameEventKind.EnemyDamaged:
                    return $"{EnemyName(run, e.ActorId, e.Subject, catalog)} took {e.Amount} from {SourceName(e.Source, catalog)}.";
                case GameEventKind.EnemyImmune:
                    return $"{EnemyName(run, e.ActorId, e.Subject, catalog)} is puffed up - <color=#B8BCC4>immune!</color>";
                case GameEventKind.EnemyDied:
                    return $"<color=#F2C94C>{SourceName(e.Source, catalog)} defeated.</color>";
                case GameEventKind.EnemyWoke:
                    return $"<color=#FF6B5E>A {SourceName(e.Source, catalog)} wakes up!</color> It acts next turn.";
                case GameEventKind.EnemyStaggered:
                    return $"{EnemyName(run, e.ActorId, e.Subject, catalog)} is staggered.";
                case GameEventKind.EnemySummoned:
                    return $"{EnemyName(run, run.Floor.EnemyAt(e.From)?.Id ?? -1, null, catalog)} summons a {SourceName(e.Source, catalog)}.";
                case GameEventKind.BossPuffed:
                    return "Lord Blobert puffs up: <color=#B8BCC4>immune for 2 turns.</color>";
                case GameEventKind.BossDeflated:
                    return "Lord Blobert deflates: <color=#F2C94C>double damage next turn!</color>";
                case GameEventKind.BossSlammed:
                    return e.Source == "lord_blobert" ? "Lord Blobert belly-slams!" : $"{SourceName(e.Source, catalog)} slams!";
                case GameEventKind.EnemyCollapsed:
                    return $"{SourceName(e.Source, catalog)} collapses into a pile of bones... <color=#F2C94C>break it before it stands up!</color>";
                case GameEventKind.EnemyReassembled:
                    return $"<color=#FF6B5E>{SourceName(e.Source, catalog)} pulls itself back together!</color>";
                case GameEventKind.EnemyCharged:
                    return $"{SourceName(e.Source, catalog)} charges!";
                case GameEventKind.BombThrown:
                    return $"<color=#FF9A2E>{SourceName(e.Source, catalog)} lobs a lit bomb!</color>";
                case GameEventKind.HeroWebbed:
                    return $"<color=#FF6B5E>You're caught in the {SourceName(e.Source, catalog)}'s web!</color> No moving or dashing next turn.";
                case GameEventKind.HeroDodged:
                    return "<color=#6CC04A>You slip aside</color> - the blow misses. Once a floor.";
                case GameEventKind.EnemyKnockedBack:
                    return $"{SourceName(e.Source, catalog)} is knocked back a tile.";
                case GameEventKind.DroneZapped:
                    return $"<color=#3FA7E0>Your drone zaps {SourceName(e.Source, catalog)} for {e.Amount}.</color>";
                case GameEventKind.KeyDropped:
                    return "<color=#F2C94C>The key clatters to the floor!</color>";
                case GameEventKind.BossEnraged:
                    return $"<color=#FF6B5E>{SourceName(e.Source, catalog)} is enraged!</color> Its blows hit 1 harder.";
                case GameEventKind.BossVanished:
                    return $"{SourceName(e.Source, catalog)} vanishes... and reappears across the stage!";
                case GameEventKind.BombArmed:
                    return "<color=#FF9A2E>A bomb is armed.</color> It explodes after your next action.";
                case GameEventKind.BombExploded:
                    return "<color=#FF9A2E>BOOM!</color>";
                case GameEventKind.SpikesTriggered:
                    return "You stepped on spikes.";
                case GameEventKind.LavaBurned:
                    return "<color=#FF9A2E>You waded through lava.</color>";
                case GameEventKind.FellThroughPit:
                    return $"<color=#FF9A2E>You drop through the pit and land hard ({e.Amount}).</color>";
                case GameEventKind.DoorsOpened:
                    return e.Amount > 0
                        ? $"<color=#F2C94C>The plate clicks: {(e.Amount == 1 ? "a door opens" : e.Amount + " doors open")}.</color>"
                        : "The plate clicks. Nothing else happens.";
                case GameEventKind.VaultEntered:
                    return "<color=#F2C94C>Into the vault.</color>";
                case GameEventKind.VaultLeft:
                    return "Back out through the door.";
                case GameEventKind.Teleported:
                    return "<color=#B084F5>The pad hums and moves you.</color>";
                case GameEventKind.FountainUsed:
                    return e.Amount > 0 ? null : "The fountain is spent.";
                case GameEventKind.KeyCollected:
                    return "<color=#F2C94C>Picked up the key.</color>";
                case GameEventKind.PotionCollected:
                    return "<color=#9FD8A0>Found a potion.</color>";
                case GameEventKind.HeroBumped:
                    return e.Source == "lurker"
                        ? "<color=#FF6B5E>Something was lurking there!</color>"
                        : "Something blocks the way.";
                case GameEventKind.ChestTapped:
                    return $"<color=#F2C94C>The lid shifts… {e.Amount} more to go.</color>";
                case GameEventKind.ChestOpened:
                    return $"<color=#F2C94C>Chest: {RewardText(e.Reward)}</color>";
                case GameEventKind.ExitUnlocked:
                    return "The exit is open.";
                case GameEventKind.ItemFound:
                    var item = catalog.Item(e.Source);
                    return $"<color=#7BD88F>Found: {item?.DisplayName ?? e.Source}!</color> It joins your inventory when the run ends.";
                case GameEventKind.CoinsFound:
                    return $"<color=#F2C94C>+{e.Amount} coins</color>";
                case GameEventKind.GemFound:
                    return "<color=#B06BE6>A gem! Blobert will not miss it.</color>";
                case GameEventKind.FloorCompleted:
                    return $"<color=#F2C94C>Floor {e.Amount} cleared!</color>";
                case GameEventKind.RunWon:
                    return "<color=#F2C94C>Run complete!</color>";
                case GameEventKind.RunLost:
                    return $"<color=#FF6B5E>{HeroName(run, catalog)} has fallen.</color>";
            }
            return null;
        }

        public static string RewardText(RewardRecord reward)
        {
            if (reward == null) return "empty";
            switch (reward.Kind)
            {
                case RewardKind.Potion: return $"+{reward.Amount} POTION";
                case RewardKind.MaxHp: return $"+{reward.Amount} MAX HP";
                default: return $"+{reward.Amount} SLASH DAMAGE";
            }
        }

        /// <summary>
        /// What the hero's slash really deals (MAINT-32). The flat field was wrong for every hero whose damage depends
        /// on the run: Rage grows with each heart lost and Bloodlust with being wounded, so those are in the low number;
        /// the bonuses that depend on which monster is struck - Ambush, Longshot, Opening Strike, Executioner, Judgement
        /// and Dawnstrike - widen it into a span, because the HUD cannot know the target.
        /// </summary>
        public static string SlashRange(RunState run, ContentCatalog catalog)
        {
            Talents.SlashSpan(run, catalog, out int low, out int high);
            return high > low ? $"{low}-{high}" : low.ToString();
        }

        public static string IntentBadge(EnemyState enemy, EnemyDefinition def, int extraDamage = 0)
        {
            switch (enemy.Intent.Kind)
            {
                case IntentKind.Attack: return $"HIT {def.Damage + extraDamage}";
                case IntentKind.Move: return "MOVE";
                case IntentKind.Fire: return $"FIRE {def.Damage + extraDamage}";
                case IntentKind.Rest: return enemy.Mode == EnemyMode.Deflated ? "x2 DMG" : "REST";
                case IntentKind.Recover: return "DAZED";
                case IntentKind.Summon: return "SUMMON";
                case IntentKind.Slam: return $"SLAM {def.SlamDamage + extraDamage}";
                case IntentKind.PuffUp: return "PUFF UP";
                case IntentKind.Charge: return $"CHARGE {def.Damage + extraDamage}";
                case IntentKind.Throw: return "BOMB";
                // Counts down to the turn it stands up again: the player's window to break it (D-058).
                case IntentKind.Reassemble: return $"BONES {enemy.ModeTurns + 1}";
                case IntentKind.Web: return "WEB";
                case IntentKind.Vanish: return "VANISH";
                default: return "";
            }
        }

        public static string IntentExplain(EnemyState enemy, EnemyDefinition def, int extraDamage = 0)
        {
            string mode = enemy.Mode == EnemyMode.Puffed ? " Puffed up: immune to damage."
                : enemy.Mode == EnemyMode.Deflated ? " Deflated: takes double damage!"
                : enemy.Mode == EnemyMode.Enraged ? " Enraged: its blows hit 1 harder." : "";
            string key = enemy.CarriesKey ? " It carries the floor's key - beat it and the key drops." : "";
            switch (enemy.Intent.Kind)
            {
                case IntentKind.Attack: return $"Next turn: hits the marked tile for {def.Damage + extraDamage}. Step off it or Shield.{mode}";
                case IntentKind.Move: return enemy.CarriesKey ? $"Next turn: runs from you.{mode}{key}" : $"Next turn: moves toward you.{mode}";
                case IntentKind.Fire: return $"Next turn: shoots fire {enemy.Intent.Dir.ToString().ToLowerInvariant()} for {def.Damage + extraDamage}. Leave the lane.{mode}";
                case IntentKind.Rest: return $"Next turn: does nothing.{mode}{key}";
                case IntentKind.Recover: return "Staggered: does nothing next turn. Free hit!";
                case IntentKind.Summon: return "Next turn: summons on the marked tiles.";
                case IntentKind.Slam: return $"Next turn: slams the marked tiles for {def.SlamDamage + extraDamage}. Dash out or Shield!";
                case IntentKind.PuffUp: return "Next turn: puffs up and becomes immune for 2 turns.";
                case IntentKind.Charge:
                    return $"Next turn: charges {enemy.Intent.Dir.ToString().ToLowerInvariant()} down the marked line for {def.Damage + extraDamage}. Get off the line or Shield!";
                case IntentKind.Throw:
                    return "Next turn: lobs a lit bomb onto the marked tile. It blows up the turn after - get clear.";
                case IntentKind.Reassemble:
                    return $"A pile of bones. Break it now - any hit will do - or it stands up again in {enemy.ModeTurns + 1} turn{(enemy.ModeTurns == 0 ? "" : "s")}.";
                case IntentKind.Web:
                    return "Next turn: spins a web onto the marked tile. Caught, you can't move or dash for a turn - Slash, Shield, drink or wait.";
                case IntentKind.Vanish:
                    return "Next turn: vanishes and reappears on the marked tile.";
                default: return (mode + key).Trim();
            }
        }

        public static string ClueExplain(Clue clue)
        {
            if (clue == Clue.Safe) return "Sensed: seems safe.";
            var text = "Sensed:";
            if ((clue & Clue.Enemy) != 0) text += "\n- Something lurks here. Clicking it wakes it.";
            if ((clue & Clue.Danger) != 0) text += "\n- A trap.";
            if ((clue & Clue.Objective) != 0) text += "\n- The key.";
            if ((clue & Clue.Exit) != 0) text += "\n- The way down.";
            if ((clue & Clue.Feature) != 0) text += "\n- Something to use: a door, a pressure plate or a teleport pad.";
            if ((clue & Clue.Treasure) != 0) text += "\n- Treasure.";
            return text;
        }
    }
}
