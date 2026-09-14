using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The only entry point presentation uses to change gameplay state. Saves at every stable boundary.
    /// </summary>
    public sealed class GameSession
    {
        readonly ISaveStore _store;

        public GameSession(ContentCatalog catalog, ISaveStore store)
        {
            Catalog = catalog;
            _store = store;
        }

        public ContentCatalog Catalog { get; }
        public RunState Run { get; private set; }
        public bool CanContinue => _store != null && _store.Exists;

        public List<GameEvent> StartNewRun(ulong seed)
        {
            var events = new List<GameEvent>();
            Run = RunFactory.NewRun(seed, Catalog, events);
            _store?.Save(Run);
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
            return true;
        }

        public CommandResult Submit(PlayerCommand command)
        {
            if (Run == null) return CommandResult.Rejected("No run in progress.");
            var result = TurnResolver.Apply(Run, command, Catalog);
            if (result.Accepted && _store != null)
            {
                if (Run.Status == RunStatus.InProgress) _store.Save(Run);
                else _store.Delete();
            }
            return result;
        }

        public void Abandon()
        {
            Run = null;
            _store?.Delete();
        }
    }
}
