using System;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Plays catalog animation frames on a UI Image, either looping (idle poses) or once (action animations).
    /// Reduced Motion holds a loop on its first frame and snaps a one-shot straight to its last frame.
    /// </summary>
    public sealed class SpriteFrameAnimator : MonoBehaviour
    {
        Image _image;
        Sprite[] _frames;
        float _fps;
        float _time;
        bool _loop = true;
        bool _done;
        Action _onComplete;

        public void Play(Image image, Sprite[] frames, float fps)
        {
            Begin(image, frames, fps, true, null);
            Apply(0);
        }

        /// <summary>Plays the frames once, stays on the last frame, then calls onComplete (if any).</summary>
        public void PlayOnce(Image image, Sprite[] frames, float fps, Action onComplete)
        {
            Begin(image, frames, fps, false, onComplete);
            if (_frames == null || _frames.Length == 0)
            {
                Finish();
                return;
            }
            if (UserPrefs.ReducedMotion)
            {
                Apply(_frames.Length - 1);
                Finish();
                return;
            }
            Apply(0);
        }

        void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>Moves the animation forward by dt seconds. Called from Update; tests call it directly.</summary>
        public void Advance(float dt)
        {
            if (_image == null || _frames == null || _frames.Length == 0 || _done) return;
            if (_loop)
            {
                if (_frames.Length < 2) return;
                if (UserPrefs.ReducedMotion)
                {
                    Apply(0);
                    return;
                }
                _time += dt;
                Apply((int)(_time * _fps) % _frames.Length);
                return;
            }

            _time += dt;
            int index = (int)(_time * _fps);
            if (index >= _frames.Length)
            {
                Apply(_frames.Length - 1);
                Finish();
                return;
            }
            Apply(index);
        }

        void Begin(Image image, Sprite[] frames, float fps, bool loop, Action onComplete)
        {
            _image = image;
            _frames = frames;
            _fps = Mathf.Max(1f, fps);
            _time = 0f;
            _loop = loop;
            _done = false;
            _onComplete = onComplete;
        }

        void Finish()
        {
            _done = true;
            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke();
        }

        void Apply(int index)
        {
            if (_image == null) return;
            var frame = _frames[index];
            if (frame != null && _image.sprite != frame) _image.sprite = frame;
        }
    }
}
