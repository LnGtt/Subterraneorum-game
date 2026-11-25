using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

public static class GameFeel
{
    private static GameFeelRunner runner;

    private static GameFeelRunner Runner
    {
        get
        {
            if (runner == null) runner = new GameObject("GameFeel").AddComponent<GameFeelRunner>();
            return runner;
        }
    }

    public static void Shake(float strength) => Runner.Shake(strength);

    public static void HitStop(float seconds) => Runner.HitStop(seconds);

    public static void DamageNumber(Vector3 worldPosition, float amount, Color color) => Runner.Number(worldPosition, amount, color);

    public static readonly Color EnemyDamageColor = new Color(1f, 0.95f, 0.75f);
    public static readonly Color PlayerDamageColor = new Color(1f, 0.35f, 0.3f);
    public static readonly Color HealColor = new Color(0.45f, 1f, 0.5f);
}

public class GameFeelRunner : MonoBehaviour
{
    private const int PoolSize = 32;
    private const float NumberLifetime = 0.75f;

    private CinemachineImpulseSource impulse;
    private float hitStopUntil;
    private bool hitStopping;
    private float lastShakeTime;
    private float lastShakeStrength;

    private readonly List<FloatingNumber> pool = new List<FloatingNumber>(PoolSize);
    private int nextNumber;
    private Font font;

    private void Awake()
    {
        impulse = gameObject.AddComponent<CinemachineImpulseSource>();
        impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        impulse.ImpulseDefinition.ImpulseDuration = 0.18f;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    public void Shake(float strength)
    {
        if (strength <= 0f || GamePause.IsPaused) return;

        if (Time.unscaledTime - lastShakeTime < 0.06f && strength <= lastShakeStrength) return;
        lastShakeTime = Time.unscaledTime;
        lastShakeStrength = strength;

        Vector2 dir = Random.insideUnitCircle.normalized;
        impulse.GenerateImpulseWithVelocity(new Vector3(dir.x, dir.y * 0.5f, dir.y) * strength);
    }

    public void HitStop(float seconds)
    {
        if (seconds <= 0f || GamePause.IsPaused) return;
        hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);
        if (!hitStopping) StartCoroutine(HitStopRoutine());
    }

    private IEnumerator HitStopRoutine()
    {
        hitStopping = true;
        Time.timeScale = 0.05f;
        while (Time.unscaledTime < hitStopUntil)
        {
            if (GamePause.IsPaused)
            {
                hitStopping = false;
                yield break;
            }

            yield return null;
        }

        if (!GamePause.IsPaused) Time.timeScale = 1f;
        hitStopping = false;
    }

    private void OnDisable()
    {
        if (hitStopping && !GamePause.IsPaused) Time.timeScale = 1f;
        hitStopping = false;
    }

    public void Number(Vector3 position, float amount, Color color)
    {
        if (amount < 0.5f) return;

        if (pool.Count < PoolSize) pool.Add(CreateNumber());
        FloatingNumber number = pool[nextNumber % pool.Count];
        nextNumber++;

        Vector2 jitter = Random.insideUnitCircle * 0.35f;
        number.Show(position + new Vector3(jitter.x, 0f, jitter.y), Mathf.RoundToInt(amount).ToString(), color, amount >= 40f);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < pool.Count; i++) pool[i].Tick(dt);
    }

    private FloatingNumber CreateNumber()
    {
        var go = new GameObject("DamageNumber", typeof(RectTransform), typeof(Canvas), typeof(Billboard));
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 0.02f;
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 80f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(go.transform, false);
        RectTransform rect = textGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Text text = textGo.GetComponent<Text>();
        text.font = font;
        text.fontSize = 54;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        textGo.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        textGo.GetComponent<Outline>().effectDistance = new Vector2(3f, -3f);

        go.SetActive(false);
        return new FloatingNumber(go, text);
    }

    private sealed class FloatingNumber
    {
        private readonly GameObject root;
        private readonly Text text;
        private Vector3 start;
        private Color color;
        private float age = NumberLifetime;
        private float scale = 1f;

        public FloatingNumber(GameObject root, Text text)
        {
            this.root = root;
            this.text = text;
        }

        public void Show(Vector3 position, string value, Color tint, bool big)
        {
            start = position;
            color = tint;
            age = 0f;
            scale = big ? 1.4f : 1f;
            text.text = value;
            text.color = tint;
            root.transform.position = position;
            root.transform.localScale = Vector3.one * 0.02f * scale;
            root.SetActive(true);
        }

        public void Tick(float dt)
        {
            if (age >= NumberLifetime) return;
            age += dt;
            float t = age / NumberLifetime;
            root.transform.position = start + Vector3.up * (0.4f + 1.1f * t);
            float pop = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
            root.transform.localScale = Vector3.one * 0.02f * scale * pop;
            text.color = new Color(color.r, color.g, color.b, 1f - Mathf.Clamp01((t - 0.55f) / 0.45f));
            if (age >= NumberLifetime) root.SetActive(false);
        }
    }
}
