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

        public static string FloorStart(FloorState floor) => floor.IsBossFloor
            ? "LORD BLOBERT: \"You dare challenge the most magnificent blob in all the dungeons?\""
            : Pick("Onward! Probably.", "Deeper we go. Bravely-ish.", "Smells like adventure. And goblin.", "Every tile tells a story. Hopefully not a sad one.");

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
                        : "Oh. Hello there.";
                    face = Expression.Shocked;
                    return 45;
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
                    return $"Lord Blobert summons a {SourceName(e.Source, catalog)}.";
                case GameEventKind.BossPuffed:
                    return "Lord Blobert puffs up: <color=#B8BCC4>immune for 2 turns.</color>";
                case GameEventKind.BossDeflated:
                    return "Lord Blobert deflates: <color=#F2C94C>double damage next turn!</color>";
                case GameEventKind.BossSlammed:
                    return "Lord Blobert belly-slams!";
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
                default: return "";
            }
        }

        public static string IntentExplain(EnemyState enemy, EnemyDefinition def, int extraDamage = 0)
        {
            string mode = enemy.Mode == EnemyMode.Puffed ? " Puffed up: immune to damage."
                : enemy.Mode == EnemyMode.Deflated ? " Deflated: takes double damage!" : "";
            switch (enemy.Intent.Kind)
            {
                case IntentKind.Attack: return $"Next turn: hits the marked tile for {def.Damage + extraDamage}. Step off it or Shield.{mode}";
                case IntentKind.Move: return $"Next turn: moves toward you.{mode}";
                case IntentKind.Fire: return $"Next turn: shoots fire {enemy.Intent.Dir.ToString().ToLowerInvariant()} for {def.Damage + extraDamage}. Leave the lane.{mode}";
                case IntentKind.Rest: return $"Next turn: does nothing.{mode}";
                case IntentKind.Recover: return "Staggered: does nothing next turn. Free hit!";
                case IntentKind.Summon: return "Next turn: summons on the marked tiles.";
                case IntentKind.Slam: return $"Next turn: slams the marked tiles for {def.SlamDamage + extraDamage}. Dash out or Shield!";
                case IntentKind.PuffUp: return "Next turn: puffs up and becomes immune for 2 turns.";
                default: return mode.Trim();
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
