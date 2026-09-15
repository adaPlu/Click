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

        public GameSession(ContentCatalog catalog, ISaveStore store, TelemetryRecorder telemetry = null)
        {
            Catalog = catalog;
            _store = store;
            Telemetry = telemetry;
        }

        public ContentCatalog Catalog { get; }
        public RunState Run { get; private set; }
        public bool CanContinue => _store != null && _store.Exists;

        /// <summary>Optional playtest recorder. Null disables telemetry; gameplay is identical either way.</summary>
        public TelemetryRecorder Telemetry { get; set; }

        public List<GameEvent> StartNewRun(ulong seed)
        {
            var events = new List<GameEvent>();
            Run = RunFactory.NewRun(seed, Catalog, events);
            _store?.Save(Run);
            Telemetry?.RunStarted(Run, events);
            return events;
        }

        public bool TryContinue(out string message)
        {
            message = null;
            if (_store == null || !_store.TryLoad(out var run, out message)) return false;
            if (run.Status != RunStatus.InProgress)
            {
                _store.Delete();
                return false;
            }
            Run = run;
            Telemetry?.RunResumed(Run);
            return true;
        }

        public CommandResult Submit(PlayerCommand command)
        {
            if (Run == null) return CommandResult.Rejected("No run in progress.");
            var pending = Telemetry?.Begin(Run, command);
            var result = TurnResolver.Apply(Run, command, Catalog);
            if (result.Accepted && _store != null)
            {
                if (Run.Status == RunStatus.InProgress) _store.Save(Run);
                else _store.Delete();
            }
            Telemetry?.Complete(pending, Run, result);
            return result;
        }

        public void Abandon()
        {
            if (Run != null) Telemetry?.RunAbandoned(Run);
            Run = null;
            _store?.Delete();
        }
    }
}
