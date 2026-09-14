using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>Tiny coroutine tweens. Reduced motion snaps instead of animating.</summary>
    public static class Tween
    {
        public static IEnumerator MoveTo(RectTransform rt, Vector2 to, float duration)
        {
            if (rt == null) yield break;
            if (UserPrefs.ReducedMotion || duration <= 0f)
            {
                rt.anchoredPosition = to;
                yield break;
            }
            var from = rt.anchoredPosition;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = to;
        }

        public static IEnumerator Popup(RectTransform rt, Graphic graphic, float rise, float duration)
        {
            if (rt == null) yield break;
            var start = rt.anchoredPosition;
            var color = graphic.color;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                float k = t / duration;
                if (!UserPrefs.ReducedMotion) rt.anchoredPosition = start + Vector2.up * (rise * Mathf.Sqrt(k));
                graphic.color = new Color(color.r, color.g, color.b, 1f - Mathf.Clamp01((k - 0.65f) / 0.35f));
                yield return null;
            }
            if (rt != null) Object.Destroy(rt.gameObject);
        }

        public static IEnumerator Punch(Transform tr, float amount, float duration)
        {
            if (tr == null || UserPrefs.ReducedMotion) yield break;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (tr == null) yield break;
                float k = t / duration;
                tr.localScale = Vector3.one * (1f + amount * Mathf.Sin(k * Mathf.PI) * (1f - k));
                yield return null;
            }
            if (tr != null) tr.localScale = Vector3.one;
        }

        public static IEnumerator Shake(RectTransform rt, float magnitude, float duration)
        {
            if (rt == null) yield break;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                float falloff = 1f - t / duration;
                rt.anchoredPosition = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (magnitude * falloff);
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = Vector2.zero;
        }

        public static IEnumerator FadeScale(RectTransform rt, CanvasGroup group, float fromScale, float duration)
        {
            if (rt == null) yield break;
            if (UserPrefs.ReducedMotion)
            {
                rt.localScale = Vector3.one;
                group.alpha = 1f;
                yield break;
            }
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                rt.localScale = Vector3.one * Mathf.Lerp(fromScale, 1f, k);
                group.alpha = k;
                yield return null;
            }
            if (rt == null) yield break;
            rt.localScale = Vector3.one;
            group.alpha = 1f;
        }

        public static IEnumerator FlyOut(RectTransform rt, Vector2 offset, float duration)
        {
            if (rt == null) yield break;
            var start = rt.anchoredPosition;
            var graphic = rt.GetComponent<Graphic>();
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                float k = t / duration;
                rt.anchoredPosition = start + offset * (1f - (1f - k) * (1f - k));
                rt.localEulerAngles = new Vector3(0f, 0f, k * 180f);
                if (graphic != null) graphic.color = graphic.color.WithAlpha(1f - k);
                yield return null;
            }
            if (rt != null) Object.Destroy(rt.gameObject);
        }
    }
}
