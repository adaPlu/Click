using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>Action animation cues, playback and poses (Unity EditMode only).</summary>
    public class ActorAnimationTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();
        GameObject _root;
        bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AnimationTestRoot", typeof(RectTransform));
            _reducedMotion = UserPrefs.ReducedMotion;
            UserPrefs.ReducedMotion = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Art.Reset();
            UserPrefs.ReducedMotion = _reducedMotion;
        }

        static Sprite MakeSprite(string name)
        {
            var sprite = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        static Sprite[] Frames(string name, int count) => Enumerable.Range(0, count).Select(i => MakeSprite($"{name}_{i}")).ToArray();

        static void UseArt(params (string key, Sprite[] frames)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<ArtCatalog>();
            catalog.SetEntries(entries.Select(e => new ArtCatalog.Entry { Key = e.key, Frames = e.frames, Fps = 10f }));
            Art.Override(catalog);
        }

        static RunState NewRun(FloorState floor)
        {
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = RunFactory.CreateHero(Catalog, ContentCatalog.DefaultHeroId),
                Floor = floor,
            };
            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
            return run;
        }

        static RunState GoblinNextToHero(out EnemyState goblin)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            goblin = EnemyAi.Spawn(floor, Catalog.Enemy("goblin"), new GridPos(3, 2), awake: true);
            return NewRun(floor);
        }

        static BoardView NewBoard(GameObject root) =>
            new BoardView((RectTransform)root.transform, root.AddComponent<SpriteFrameAnimator>(), Vector2.zero);

        static void Render(BoardView board, RunState run) =>
            board.Render(run, Catalog, new List<Threat>(), new HashSet<GridPos>(), false, null, false);

        static Transform Named(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        static Transform AnimChild(Transform token) =>
            token.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith("Anim "));

        [Test]
        public void PickKeepsTheMostImportantEventPerToken()
        {
            var run = GoblinNextToHero(out var goblin);
            var cues = ActorAnimations.Pick(run, new List<GameEvent>
            {
                GameEvent.Of(GameEventKind.HeroMoved),
                GameEvent.Of(GameEventKind.HeroDamaged, source: "goblin"),
                GameEvent.Of(GameEventKind.EnemyMoved, goblin.Id),
                GameEvent.Of(GameEventKind.EnemyAttacked, goblin.Id, source: "goblin"),
                GameEvent.Of(GameEventKind.EnemyStaggered, goblin.Id),
            });

            Assert.That(cues.Count, Is.EqualTo(2));
            Assert.That(cues[ActorAnimations.HeroToken].Animation, Is.EqualTo("hit"));
            Assert.That(cues[ActorAnimations.HeroToken].ContentId, Is.EqualTo(ContentCatalog.DefaultHeroId));
            Assert.That(cues[goblin.Id].Animation, Is.EqualTo("attack"));
            Assert.That(cues[goblin.Id].ContentId, Is.EqualTo("goblin"));
        }

        [Test]
        public void SummonCuesTheBossAndTheMinion()
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = Catalog.RunFloorCount;
            floor.IsBossFloor = true;
            floor.Start = new GridPos(0, 2);
            var boss = EnemyAi.Spawn(floor, Catalog.Enemy("lord_blobert"), new GridPos(4, 2), awake: true);
            var minion = EnemyAi.Spawn(floor, Catalog.Enemy("slimelet"), new GridPos(4, 1), awake: true);
            var run = NewRun(floor);

            var cues = ActorAnimations.Pick(run, new List<GameEvent>
            {
                GameEvent.Of(GameEventKind.EnemySummoned, minion.Id, boss.Pos, minion.Pos, source: "slimelet"),
            });

            Assert.That(cues[boss.Id].Animation, Is.EqualTo("summon"));
            Assert.That(cues[minion.Id].Animation, Is.EqualTo("spawn"));
            Assert.That(cues[minion.Id].ContentId, Is.EqualTo("slimelet"));
        }

        [Test]
        public void AnimationsFallBackToSimilarOnesAndPosesFollowIntent()
        {
            Assert.That(ActorAnimations.Chain("fire"), Is.EqualTo(new[] { "fire", "attack" }));
            Assert.That(ActorAnimations.Chain("immune"), Is.EqualTo(new[] { "immune", "hit" }));
            Assert.That(ActorAnimations.Chain("dash"), Is.EqualTo(new[] { "dash", "step" }));
            Assert.That(ActorAnimations.Chain("slash"), Is.EqualTo(new[] { "slash" }));
            Assert.That(ActorAnimations.Holds(ActorAnimations.HeroToken, "defeat"), Is.True);
            Assert.That(ActorAnimations.Holds(3, "defeat"), Is.False);

            var slime = new EnemyState { Intent = Intent.Rest() };
            Assert.That(ActorAnimations.Pose(slime, Catalog.Enemy("crowned_slime")), Is.EqualTo("rest"));
            var boss = new EnemyState { Intent = Intent.Slam(new GridPos(1, 1)) };
            Assert.That(ActorAnimations.Pose(boss, Catalog.Enemy("lord_blobert")), Is.EqualTo("boast"));
            boss.Mode = EnemyMode.Puffed;
            Assert.That(ActorAnimations.Pose(boss, Catalog.Enemy("lord_blobert")), Is.EqualTo(""));
        }

        [Test]
        public void OneShotPlaysOverTheTokenThenRestoresIt()
        {
            var run = GoblinNextToHero(out var goblin);
            var frames = Frames("attack", 3);
            UseArt((ArtKeys.Actor("goblin", "attack"), frames));

            var board = NewBoard(_root);
            board.QueueActorAnimations(run, new List<GameEvent> { GameEvent.Of(GameEventKind.EnemyAttacked, goblin.Id, source: "goblin") });
            Render(board, run);

            var token = Named(_root, $"Enemy {goblin.Id}");
            var anim = AnimChild(token);
            Assert.That(anim, Is.Not.Null);
            Assert.That(token.Find("Body").gameObject.activeSelf, Is.False);
            var image = anim.GetComponent<Image>();
            Assert.That(image.sprite, Is.SameAs(frames[0]));

            var animator = anim.GetComponent<SpriteFrameAnimator>();
            animator.Advance(0.15f);
            Assert.That(image.sprite, Is.SameAs(frames[1]));
            animator.Advance(1f);

            Assert.That(AnimChild(token), Is.Null, "The one-shot removes itself when done.");
            Assert.That(token.Find("Body").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ReducedMotionSkipsOneShotsButHoldsFinalPoses()
        {
            UserPrefs.ReducedMotion = true;
            var defeat = Frames("defeat", 2);
            UseArt((ArtKeys.Actor(ArtKeys.HeroId, "hit"), Frames("hit", 3)), (ArtKeys.Actor(ArtKeys.HeroId, "defeat"), defeat));

            var run = GoblinNextToHero(out _);
            var board = NewBoard(_root);
            board.QueueActorAnimations(run, new List<GameEvent> { GameEvent.Of(GameEventKind.HeroDamaged, source: "goblin") });
            Render(board, run);
            Assert.That(AnimChild(Named(_root, "Hero")), Is.Null);

            var defeatRoot = new GameObject("DefeatRoot", typeof(RectTransform));
            try
            {
                var defeatBoard = NewBoard(defeatRoot);
                defeatBoard.QueueActorAnimations(run, new List<GameEvent> { GameEvent.Of(GameEventKind.RunLost) });
                Render(defeatBoard, run);
                var hero = Named(defeatRoot, "Hero");
                var anim = AnimChild(hero);
                Assert.That(anim, Is.Not.Null);
                Assert.That(anim.GetComponent<Image>().sprite, Is.SameAs(defeat[1]), "Reduced Motion shows the final frame.");
                Assert.That(hero.Find("Body").gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(defeatRoot);
            }
        }

        [Test]
        public void EnemyDefeatPlaysBeforeTheTokenFades()
        {
            var run = GoblinNextToHero(out var goblin);
            var defeat = Frames("goblin_defeat", 4);
            UseArt((ArtKeys.Actor("goblin", "defeat"), defeat));

            var board = NewBoard(_root);
            Render(board, run);
            run.Floor.Enemies.Clear();
            board.QueueActorAnimations(run, new List<GameEvent> { GameEvent.Of(GameEventKind.EnemyDied, goblin.Id, source: "goblin") });
            Render(board, run);

            var token = Named(_root, $"Enemy {goblin.Id}");
            var anim = AnimChild(token);
            Assert.That(anim, Is.Not.Null);
            Assert.That(anim.GetComponent<Image>().sprite, Is.SameAs(defeat[0]));
            Assert.That(token.GetComponent<CanvasGroup>(), Is.Not.Null, "The token still fades out after the animation.");
        }

        [Test]
        public void GuardRestAndBoastPosesUseArt()
        {
            var guard = MakeSprite("guard");
            var rest = MakeSprite("rest");
            var boast = MakeSprite("boast");
            UseArt((ArtKeys.Actor(ArtKeys.HeroId, "guard"), new[] { guard }), (ArtKeys.Actor("crowned_slime", "rest"), new[] { rest }),
                (ArtKeys.Actor("lord_blobert", "boast"), new[] { boast }));

            var heroRoot = UiFactory.Rect(_root.transform, "HeroPose");
            Icons.Hero(heroRoot, true);
            Assert.That(heroRoot.GetComponentsInChildren<Image>().Any(i => i.sprite == guard), Is.True);
            Assert.That(heroRoot.GetComponentsInChildren<Image>().Any(i => i.sprite == Shapes.Ring), Is.True, "Guard ring stays readable.");

            var slimeRoot = UiFactory.Rect(_root.transform, "SlimePose");
            Icons.Enemy(slimeRoot, Catalog.Enemy("crowned_slime"), EnemyMode.Normal, IntentKind.Rest);
            Assert.That(slimeRoot.GetComponentsInChildren<Image>().Any(i => i.sprite == rest), Is.True);

            var bossRoot = UiFactory.Rect(_root.transform, "BossPose");
            Icons.Enemy(bossRoot, Catalog.Enemy("lord_blobert"), EnemyMode.Normal, IntentKind.Slam);
            Assert.That(bossRoot.GetComponentsInChildren<Image>().Any(i => i.sprite == boast), Is.True);

            var wired = ArtKeys.Wired(Catalog);
            // The default hero's poses: Ironheart since D-057, which retired the mascot from play.
            foreach (var key in new[] { ArtKeys.Actor(ArtKeys.HeroId, "guard"), ArtKeys.Actor(ArtKeys.HeroId, "potion"), "actor_goblin_attack", "actor_fire_imp_fire", "actor_lord_blobert_puffup" })
                Assert.That(wired, Does.Contain(key));
            Assert.That(wired, Is.Unique);
        }
    }
}
