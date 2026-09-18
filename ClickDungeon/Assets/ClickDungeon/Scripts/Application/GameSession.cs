using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The only entry point presentation uses to change gameplay state. Saves at every stable boundary
    /// and reports each transaction to playtest telemetry when enabled.
    /// </summary>
    public sealed class GameSession
    {
        readonly ISaveStore _store;
        readonly IProfileStore _profiles;
        TelemetryRecorder _telemetry;

        public GameSession(ContentCatalog catalog, ISaveStore store, TelemetryRecorder telemetry = null,
            IProfileStore profiles = null)
        {
            Catalog = catalog;
            _store = store;
            _profiles = profiles ?? new MemoryProfileStore();
            Profile = _profiles.Load();
            Telemetry = telemetry;
        }

        /// <summary>What the player keeps between runs (D-025). Never read during a run: provisions become hero numbers at the start.</summary>
        public ProfileState Profile { get; private set; }

        /// <summary>Writes the profile. Presentation calls this after spending in the shop.</summary>
        public void SaveProfile()
        {
            if (_profiles == null || Profile == null) return;
            Guarded(() => _profiles.Save(Profile));
        }

        /// <summary>Content tuned for the current run's difficulty. Changes when a run of another tier starts or resumes.</summary>
        public ContentCatalog Catalog { get; private set; }
        public RunState Run { get; private set; }
        public bool CanContinue => _store != null && _store.Exists;

        /// <summary>
        /// Why the last save or delete failed, or null. Store failures never interrupt play: the run continues in memory and
        /// presentation warns the player that it may not resume.
        /// </summary>
        public string SaveError { get; private set; }

        /// <summary>Optional playtest recorder. Null disables telemetry; gameplay is identical either way.</summary>
        public TelemetryRecorder Telemetry
        {
            get => _telemetry;
            set
            {
                _telemetry = value;
                if (value != null) value.Catalog = Catalog;
            }
        }

        /// <summary>Starts a run at the current catalog's difficulty.</summary>
        public List<GameEvent> StartNewRun(ulong seed) => StartNewRun(seed, Catalog.Difficulty);

        public List<GameEvent> StartNewRun(ulong seed, Difficulty difficulty) => StartNewRun(seed, difficulty, MovementMode.Free);

        public List<GameEvent> StartNewRun(ulong seed, Difficulty difficulty, MovementMode movement) =>
            StartNewRun(seed, difficulty, movement, ContentCatalog.DefaultHeroId);

        public List<GameEvent> StartNewRun(ulong seed, Difficulty difficulty, MovementMode movement, string heroId)
        {
            UseCatalog(Catalog.ForDifficulty(difficulty));
            var events = new List<GameEvent>();
            Run = RunFactory.NewRun(seed, Catalog, events, heroId, movement);
            // Provisions are spent into the run's own numbers, so the simulation stays a function of its inputs.
            ProfileSystem.Provision(Profile, Run, Catalog);
            Progression.Apply(Profile, Run, Catalog);
            Inventory.Apply(Profile, Run, Catalog);
            SaveProfile();
            Persist();
            Telemetry?.RunStarted(Run, events);
            return events;
        }

        public bool TryContinue(out string message)
        {
            message = null;
            if (_store == null || !_store.TryLoad(out var run, out message)) return false;
            if (run.Status != RunStatus.InProgress)
            {
                Guarded(_store.Delete);
                return false;
            }

            var catalog = Catalog.ForDifficulty(run.Difficulty);
            var problem = ContentProblem(run, catalog);
            if (problem != null)
            {
                message = $"This save can't be continued with this version of the game ({problem}). Start a new run instead.";
                return false;
            }

            UseCatalog(catalog);
            Run = run;
            Telemetry?.RunResumed(Run);
            return true;
        }

        public CommandResult Submit(PlayerCommand command)
        {
            if (Run == null) return CommandResult.Rejected("No run in progress.");
            var pending = Telemetry?.Begin(Run, command);
            var wasInProgress = Run.Status == RunStatus.InProgress;
            var result = TurnResolver.Apply(Run, command, Catalog);
            if (result.Accepted) Persist();
            // The treasure carried out is banked once, on the turn the run ends.
            if (wasInProgress && Run.Status != RunStatus.InProgress)
            {
                ProfileSystem.Bank(Profile, Run);
                Inventory.Bank(Profile, Run, Catalog);
                SaveProfile();
            }
            Telemetry?.Complete(pending, Run, result);
            return result;
        }

        /// <summary>Giving up still carries out what was found: abandoning is not a way to lose coins, nor to farm them twice.</summary>
        public void Abandon()
        {
            if (Run != null)
            {
                Telemetry?.RunAbandoned(Run);
                if (Run.Status == RunStatus.InProgress)
                {
                    ProfileSystem.Bank(Profile, Run);
                    Inventory.Bank(Profile, Run, Catalog);
                    SaveProfile();
                }
            }
            Run = null;
            SaveError = null;
            if (_store != null) Guarded(_store.Delete);
        }

        /// <summary>
        /// Saves a run in progress, or clears the save of a finished one. A finished run is written before the delete, so a
        /// delete that fails part-way leaves a finished save that Continue discards, never the last turn before the end.
        /// </summary>
        void Persist()
        {
            if (_store == null) return;
            SaveError = null;
            if (Run.Status == RunStatus.InProgress)
            {
                Guarded(() => _store.Save(Run));
                return;
            }
            Guarded(() => _store.Save(Run));
            if (Guarded(_store.Delete)) SaveError = null;
        }

        bool Guarded(Action storeAction)
        {
            try
            {
                storeAction();
                return true;
            }
            catch (Exception ex)
            {
                SaveError = SaveError ?? ex.Message;
                return false;
            }
        }

        /// <summary>Content the saved run refers to but this build's catalog lacks (for example after a content id was renamed).</summary>
        static string ContentProblem(RunState run, ContentCatalog catalog)
        {
            if (run.ContentCatalogVersion > catalog.Version) return "it was made by a newer version";
            if (!catalog.HeroClasses.ContainsKey(run.Hero.ClassId) || !catalog.HeroIdentities.ContainsKey(run.Hero.IdentityId)) return "unknown hero";
            foreach (var enemy in run.Floor.Enemies)
                if (!catalog.HasEnemy(enemy.DefId)) return $"unknown enemy '{enemy.DefId}'";
            return null;
        }

        void UseCatalog(ContentCatalog catalog)
        {
            Catalog = catalog;
            if (_telemetry != null) _telemetry.Catalog = Catalog;
        }
    }
}
