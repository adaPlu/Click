using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>Loops catalog animation frames on a UI Image. Reduced Motion holds the first frame.</summary>
    public sealed class SpriteFrameAnimator : MonoBehaviour
    {
        Image _image;
        Sprite[] _frames;
        float _fps;
        float _time;

        public void Play(Image image, Sprite[] frames, float fps)
        {
            _image = image;
            _frames = frames;
            _fps = Mathf.Max(1f, fps);
            _time = 0f;
            Apply(0);
        }

        void Update()
        {
            if (_image == null || _frames == null || _frames.Length < 2) return;
            if (UserPrefs.ReducedMotion)
            {
                Apply(0);
                return;
            }
            _time += Time.unscaledDeltaTime;
            Apply((int)(_time * _fps) % _frames.Length);
        }

        void Apply(int index)
        {
            var frame = _frames[index];
            if (frame != null && _image.sprite != frame) _image.sprite = frame;
        }
    }
}
