using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// What the hero carries and spends on a turn (D-082). The game had one of these before now - the potion - and it
    /// is not in here: it predates the idea, owns its own command and its own button, and moving it would change what
    /// every save already means.
    ///
    /// The single rule this exists to hold is the ward's: a resurrection is either IN PLACE or it is not, and nothing
    /// may put a second one beside it. That rule lives on <see cref="HeroState.Ward"/> - a number the hero carries -
    /// so every road to a resurrection obeys it without knowing about the others: the Cleric's capstone placing one on
    /// arrival, a feather spent from the pack, and whatever places one next.
    /// </summary>
    public static class Usables
    {
        /// <summary>What is in this pack slot, or null when the slot is empty or holds something the catalogue lost.</summary>
        public static UsableDefinition InSlot(RunState run, ContentCatalog catalog, int slot)
        {
            var pack = run?.Hero?.Usables;
            if (pack == null || slot < 0 || slot >= pack.Count) return null;
            var carried = pack[slot];
            return carried == null || carried.Charges <= 0 ? null : catalog.UsableOrNull(carried.Id);
        }

        /// <summary>
        /// Whether this can be spent right now, and why not. The one place that decides it, so the button, the
        /// resolver and the bot cannot disagree - the same reason <see cref="Commands"/> exists.
        ///
        /// Note what is NOT refused here: spending a ward while one is already in place. That is a legal thing to do
        /// and it wastes the charge, which is the rule as asked for. The bot declines it in
        /// <c>AutoPlayer.LegalCommands</c>, where declining is a matter of playing well rather than of what the game
        /// allows.
        /// </summary>
        public static bool CanUse(RunState run, ContentCatalog catalog, int slot, out string reason)
        {
            reason = null;
            var usable = InSlot(run, catalog, slot);
            if (usable == null) return Fail(out reason, "Nothing in that pocket.");
            return true;
        }

        /// <summary>Spends it. The caller has already validated; this takes the charge and does the thing.</summary>
        public static void Use(RunState run, ContentCatalog catalog, int slot, List<GameEvent> events)
        {
            var usable = InSlot(run, catalog, slot);
            if (usable == null) return;
            var hero = run.Hero;
            var carried = hero.Usables[slot];

            // The charge goes whatever happens. A feather spent on a hero who is already warded is a feather gone.
            carried.Charges--;
            events.Add(GameEvent.Of(GameEventKind.UsableSpent, to: hero.Pos, amount: carried.Charges, source: usable.Id));
            if (carried.Charges <= 0) hero.Usables.RemoveAt(slot);

            switch (usable.Effect)
            {
                case UsableEffect.Ward:
                    // The same question Place asks, asked once (REL-94). This used to refuse on ANY ward, which made
                    // Place's documented "a better one replaces a weaker one" branch unreachable and left two rules
                    // answering one question. It also meant the four classes whose capstone places a ward every floor
                    // could never use a feather at all: they were permanently occupied, so a 250-coin purchase was
                    // destroyed at any ordinary moment (REL-93). A feather is STRONGER than a capstone's ward, so it
                    // upgrades one; only a ward at least as good as this one wastes the charge.
                    if (hero.Ward >= usable.Amount)
                    {
                        events.Add(GameEvent.Of(GameEventKind.WardWasted, to: hero.Pos, amount: hero.Ward, source: usable.Id));
                        break;
                    }
                    Place(hero, usable.Amount, usable.Id, events);
                    break;
            }
        }

        /// <summary>
        /// Puts a resurrection in place, if a better one is not there already. Every road to one comes through here,
        /// which is what makes "only one at a time" true rather than merely intended.
        /// </summary>
        public static void Place(HeroState hero, int hearts, string source, List<GameEvent> events)
        {
            if (hero == null || hearts <= 0 || hero.Ward >= hearts) return;
            hero.Ward = hearts;
            events?.Add(GameEvent.Of(GameEventKind.WardPlaced, to: hero.Pos, amount: hearts, source: source));
        }

        /// <summary>
        /// Whether spending this would buy NOTHING - the charge goes and the game is unchanged. The rules allow it;
        /// this is the one place that knows it is pointless, so the bot's refusal to do it and the button's warning
        /// that it is about to happen cannot drift apart (REL-92, REL-94).
        /// </summary>
        public static bool WouldWaste(RunState run, UsableDefinition usable) =>
            usable != null && usable.Effect == UsableEffect.Ward
            && run?.Hero != null && run.Hero.Ward >= usable.Amount;

        /// <summary>
        /// Places the ward a hero's DivineShield talent grants them, if they have one. The `1 + amount` conversion
        /// lives HERE and nowhere else (TEST-107): it used to be written out at both production call sites AND a third
        /// time in a test helper, and a test that re-implements the thing it is checking cannot fail. Both call sites
        /// and the helper now go through this, so changing the conversion changes every one of them at once.
        /// </summary>
        public static void PlaceTalentWard(RunState run, List<GameEvent> events)
        {
            int talent = run?.Perk(TalentEffect.DivineShield) ?? 0;
            // Guarded: without it a hero with no talent is armed with a one-heart resurrection.
            if (talent > 0) Place(run.Hero, 1 + talent, "divine_shield", events);
        }

        /// <summary>Puts one in the pack, stacking onto a pocket that already holds the same thing.</summary>
        public static void Give(HeroState hero, UsableDefinition usable, int count = 1)
        {
            if (hero == null || usable == null || count <= 0) return;
            if (hero.Usables == null) hero.Usables = new List<CarriedUsable>();
            foreach (var carried in hero.Usables)
                if (carried.Id == usable.Id)
                {
                    carried.Charges += usable.Charges * count;
                    return;
                }
            hero.Usables.Add(new CarriedUsable { Id = usable.Id, Charges = usable.Charges * count });
        }

        /// <summary>
        /// One thing the last slot of the action bar can hold (D-083). The potion is always the first and is always
        /// there even at zero, so the bar never loses a button and the row keeps the shape the reference art draws.
        /// </summary>
        public readonly struct Pocket
        {
            /// <summary>The usable's id, or null when this is the potion - which is not a usable and never will be.</summary>
            public readonly string UsableId;
            public readonly string Label;
            public readonly int Count;
            /// <summary>Index into the hero's pack, or -1 for the potion.</summary>
            public readonly int Slot;

            public Pocket(string usableId, string label, int count, int slot)
            {
                UsableId = usableId;
                Label = label;
                Count = count;
                Slot = slot;
            }

            public bool IsPotion => UsableId == null;

            /// <summary>What pressing the button sends. One place, so the button and the rules cannot disagree.</summary>
            public PlayerCommand Command => IsPotion ? PlayerCommand.Potion() : PlayerCommand.Use(Slot);
        }

        /// <summary>
        /// What the last slot can be swapped between, in the order it cycles: the potion, then the pack. Empty pockets
        /// and ids the catalogue has lost are left out, so the list is only ever things that can actually be pressed.
        /// </summary>
        public static List<Pocket> Pockets(RunState run, ContentCatalog catalog)
        {
            var pockets = new List<Pocket> { new Pocket(null, "POTION", run?.Hero?.Potions ?? 0, -1) };
            var pack = run?.Hero?.Usables;
            if (pack == null || catalog == null) return pockets;
            for (int i = 0; i < pack.Count; i++)
            {
                var carried = pack[i];
                if (carried == null || carried.Charges <= 0) continue;
                var def = catalog.UsableOrNull(carried.Id);
                if (def == null) continue;
                pockets.Add(new Pocket(def.Id, (def.DisplayName ?? def.Id).ToUpperInvariant(), carried.Charges, i));
            }
            return pockets;
        }

        static bool Fail(out string reason, string why)
        {
            reason = why;
            return false;
        }
    }
}
