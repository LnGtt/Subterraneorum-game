using UnityEngine;
using UnityEngine.Rendering;

public class LevelUpEffect : MonoBehaviour
{
    public const float Duration = 1.3f;

    private static readonly Color Gold = new Color(1f, 0.84f, 0.35f);

    private Transform target;
    private Light flash;
    private TextMesh label;
    private float elapsed;

    public static void Play(Transform target, int level)
    {
        if (target == null) return;

        var go = new GameObject("LevelUpEffect");
        go.transform.position = target.position;
        LevelUpEffect effect = go.AddComponent<LevelUpEffect>();
        effect.target = target;
        effect.Build(level);
    }

    private void Build(int level)
    {
        GroundTelegraph.Spawn(target.position, 2.2f, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 0.9f, GroundTelegraph.Shape.Ring, growFill: true, follow: target);
        GameFeel.Shake(0.15f);

        Material spark = RuntimeAssets.Spark;
        if (spark != null) CreateSparks(spark);

        var lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * 1.5f;
        flash = lightGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = Gold;
        flash.range = 7f;
        flash.intensity = 0f;
        flash.shadows = LightShadows.None;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        label = textGo.AddComponent<TextMesh>();
        label.text = $"NÍVEL {level}!";
        label.font = UiFactory.Font;
        label.fontSize = 72;
        label.characterSize = 0.06f;
        label.fontStyle = FontStyle.Bold;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = Gold;
        MeshRenderer textRenderer = textGo.GetComponent<MeshRenderer>();
        textRenderer.sharedMaterial = UiFactory.Font.material;
        textRenderer.shadowCastingMode = ShadowCastingMode.Off;

        Update();
    }

    private void CreateSparks(Material material)
    {
        var sparksGo = new GameObject("Sparks");
        sparksGo.transform.SetParent(transform, false);
        sparksGo.transform.localPosition = Vector3.up * 0.1f;
        ParticleSystem ps = sparksGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Gold, new Color(1f, 0.97f, 0.75f));
        main.gravityModifier = 0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 50f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.7f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = sparksGo.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        if (target != null) transform.position = target.position;

        float t = Mathf.Clamp01(elapsed / Duration);
        if (flash != null) flash.intensity = 4f * Mathf.Sin(Mathf.Min(1f, t * 1.6f) * Mathf.PI);

        if (label != null)
        {
            label.transform.localPosition = Vector3.up * (2.4f + 0.9f * Mathf.Sqrt(t));
            Camera cam = Camera.main;
            if (cam != null) label.transform.rotation = cam.transform.rotation;
            float alpha = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;
            label.color = new Color(Gold.r, Gold.g, Gold.b, alpha);
        }

        if (elapsed >= Duration + 0.8f) Destroy(gameObject);
    }
}
