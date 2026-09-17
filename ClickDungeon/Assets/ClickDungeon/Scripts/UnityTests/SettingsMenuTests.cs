using System.Linq;
using ClickDungeon.Domain;
using ClickDungeon.Unity;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.UnityTests
{
    /// <summary>The settings menu's movement picker (Unity EditMode only).</summary>
    public class SettingsMenuTests
    {
        GameObject _root;
        MovementMode _savedMovement;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SettingsTestRoot", typeof(RectTransform));
            var empty = ScriptableObject.CreateInstance<ArtCatalog>();
            empty.SetEntries(Enumerable.Empty<ArtCatalog.Entry>());
            Art.Override(empty);
            // The picker writes a real preference; keep the developer's own setting intact.
            _savedMovement = UserPrefs.Movement;
        }

        [TearDown]
        public void TearDown()
        {
            UserPrefs.Movement = _savedMovement;
            Object.DestroyImmediate(_root);
            Art.Reset();
        }

        Button[] Buttons() => _root.GetComponentsInChildren<Button>(true);

        [Test]
        public void MovementPickerShowsAndTogglesTheModeForTheNextRun()
        {
            UserPrefs.Movement = MovementMode.Free;
            var modal = new ModalOverlay((RectTransform)_root.transform, _root.AddComponent<SpriteFrameAnimator>());
            Menus.OpenSettings(modal, () => { });

            Assert.That(Buttons().Select(b => b.name), Does.Contain("MOVEMENT: FREE ROAM"));
            var body = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "Body").text;
            Assert.That(body, Does.Contain("next new run"), "The menu says a run in progress keeps its own mode.");

            Buttons().First(b => b.name == "MOVEMENT: FREE ROAM").onClick.Invoke();
            Assert.That(UserPrefs.Movement, Is.EqualTo(MovementMode.Step));
            Assert.That(Buttons().Select(b => b.name), Does.Contain("MOVEMENT: STEP BY STEP"), "The menu redraws with the new mode.");

            Buttons().First(b => b.name == "MOVEMENT: STEP BY STEP").onClick.Invoke();
            Assert.That(UserPrefs.Movement, Is.EqualTo(MovementMode.Free));
        }
    }
}
