using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    public enum ThreatKind { Attack, Fire, Slam, Summon, BombBlast, BombArmed }

    public struct Threat
    {
        public ThreatKind Kind;
        public GridPos Cell;
        public int Damage;
        public int SourceId;
    }

    /// <summary>
    /// Read-only preview of what already-declared intents and armed bombs will do in the next turn.
    /// Presentation draws telegraphs from this; it never decides outcomes.
    /// </summary>
    public static class Threats
    {
        public static List<Threat> Compute(RunState run, ContentCatalog catalog)
        {
            var threats = new List<Threat>();
            var floor = run.Floor;

            foreach (var enemy in floor.Enemies)
            {
                if (!enemy.Awake) continue;
                var def = catalog.Enemy(enemy.DefId);
                var intent = enemy.Intent;
                switch (intent.Kind)
                {
                    case IntentKind.Attack:
                        Add(threats, ThreatKind.Attack, intent.Target, def.Damage, enemy.Id);
                        break;
                    case IntentKind.Fire:
                        foreach (var cell in Board.LaneCells(run, enemy.Pos, intent.Dir, def.Range))
                            Add(threats, ThreatKind.Fire, cell, def.Damage, enemy.Id);
                        break;
                    case IntentKind.Slam:
                        foreach (var cell in Board.SlamCells(intent.Target, def.SlamShakesLines))
                            Add(threats, ThreatKind.Slam, cell, def.SlamDamage, enemy.Id);
                        break;
                    case IntentKind.Summon:
                        Add(threats, ThreatKind.Summon, intent.Target, 0, enemy.Id);
                        foreach (var cell in EnemyAi.SummonCells(run, enemy, def, intent.Target))
                            if (cell != intent.Target) Add(threats, ThreatKind.Summon, cell, 0, enemy.Id);
                        break;
                }
            }

            var tuning = catalog.Hazards;
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (!cell.BombArmed) continue;
                bool imminent = cell.BombFuse == 0;
                foreach (var blast in Board.BlastCells(p, tuning.BombRadius))
                    Add(threats, imminent ? ThreatKind.BombBlast : ThreatKind.BombArmed, blast, imminent ? tuning.BombDamage : 0, 0);
            }

            return threats;
        }

        public static int DamageAt(List<Threat> threats, GridPos cell)
        {
            int total = 0;
            foreach (var threat in threats)
                if (threat.Cell == cell) total += threat.Damage;
            return total;
        }

        static void Add(List<Threat> threats, ThreatKind kind, GridPos cell, int damage, int sourceId)
        {
            if (!cell.InBounds) return;
            threats.Add(new Threat { Kind = kind, Cell = cell, Damage = damage, SourceId = sourceId });
        }
    }
}
