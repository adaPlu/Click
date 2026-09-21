using System.Collections.Generic;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    public enum GameEventKind
    {
        FloorStarted,
        CellRevealed,
        CellSensed,
        HeroMoved,
        HeroDashed,
        HeroWaited,
        HeroSlashed,
        HeroShielded,
        HeroDamaged,
        HeroBlocked,
        HeroHealed,
        ChestTapped,
        HeroBumped,
        EnemyWoke,
        EnemyMoved,
        EnemyAttacked,
        EnemyMissed,
        EnemyFired,
        EnemyDamaged,
        EnemyImmune,
        EnemyStaggered,
        EnemyDied,
        EnemySummoned,
        BossPuffed,
        BossDeflated,
        BossSlammed,
        BombArmed,
        BombExploded,
        SpikesTriggered,
        LavaBurned,
        FellThroughPit,
        KeyCollected,
        PotionCollected,
        DoorsOpened,
        VaultEntered,
        VaultLeft,
        Teleported,
        FountainUsed,
        ChestOpened,
        ExitUnlocked,
        CoinsFound,
        ItemFound,
        GemFound,
        FloorCompleted,
        RunWon,
        RunLost,
        // First expansion monsters (D-058), appended so no existing kind changes its value.
        EnemyCollapsed,
        EnemyReassembled,
        EnemyCharged,
        BombThrown,
    }

    /// <summary>A record of something the simulation already decided. Presentation only reacts to these.</summary>
    public sealed class GameEvent
    {
        public GameEventKind Kind;
        public int ActorId;
        public GridPos From = GridPos.Invalid;
        public GridPos To = GridPos.Invalid;
        public int Amount;
        public string Source;
        /// <summary>Content id of the affected actor, so presentation can name it even after it was removed.</summary>
        public string Subject;
        public Clue Clue;
        public RewardRecord Reward;

        public static GameEvent Of(GameEventKind kind, int actorId = 0, GridPos? from = null, GridPos? to = null,
            int amount = 0, string source = null, string subject = null)
        {
            return new GameEvent
            {
                Kind = kind,
                ActorId = actorId,
                From = from ?? GridPos.Invalid,
                To = to ?? GridPos.Invalid,
                Amount = amount,
                Source = source,
                Subject = subject,
            };
        }

        public override string ToString() => $"{Kind} actor={ActorId} {From}->{To} amount={Amount} {Source}";
    }

    public struct PlayerCommand
    {
        public CommandKind Kind;
        public GridPos Target;

        public PlayerCommand(CommandKind kind, GridPos target)
        {
            Kind = kind;
            Target = target;
        }

        public static PlayerCommand Move(GridPos target) => new PlayerCommand(CommandKind.Move, target);
        public static PlayerCommand Wait() => new PlayerCommand(CommandKind.Wait, GridPos.Invalid);
        public static PlayerCommand Slash(GridPos target) => new PlayerCommand(CommandKind.Slash, target);
        public static PlayerCommand Shield() => new PlayerCommand(CommandKind.Shield, GridPos.Invalid);
        public static PlayerCommand Dash(GridPos target) => new PlayerCommand(CommandKind.Dash, target);
        public static PlayerCommand Potion() => new PlayerCommand(CommandKind.Potion, GridPos.Invalid);
        public static PlayerCommand Interact(GridPos target) => new PlayerCommand(CommandKind.Interact, target);

        public override string ToString() => $"{Kind}{Target}";
    }

    public sealed class CommandResult
    {
        public bool Accepted;
        public string RejectReason;
        public List<GameEvent> Events = new List<GameEvent>();

        public static CommandResult Rejected(string reason) => new CommandResult { Accepted = false, RejectReason = reason };
    }
}
