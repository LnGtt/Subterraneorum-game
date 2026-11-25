using UnityEngine;

public class GroundTelegraph : MonoBehaviour
{
    public enum Shape
    {
        Disc,
        Ring,
        Cone,
    }

    private static readonly System.Collections.Generic.Dictionary<int, Mesh> coneMeshes = new System.Collections.Generic.Dictionary<int, Mesh>();
    private float coneAngle = 90f;
    private bool rotateWithFollow;

    public static GroundTelegraph SpawnCone(Vector3 center, Vector3 forward, float radius, float angle, Color color, float duration, Transform follow = null)
    {
        var go = new GameObject("Telegraph_Cone");
        go.layer = 2;
        GroundTelegraph telegraph = go.AddComponent<GroundTelegraph>();
        telegraph.coneAngle = Mathf.Clamp(angle, 5f, 360f);
        telegraph.rotateWithFollow = follow != null;
        forward.y = 0f;
        go.transform.rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;
        telegraph.Setup(center, radius, color, duration, Shape.Cone, true, follow);
        return telegraph;
    }

    private const int Segments = 48;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static Mesh discMesh;
    private static Mesh ringMesh;

    private Transform fill;
    private MeshRenderer outlineRenderer;
    private MeshRenderer fillRenderer;
    private MaterialPropertyBlock block;
    private Color color;
    private float duration;
    private float elapsed;
    private Transform follow;
    private bool growFill;

    public float Radius { get; private set; }

    public static GroundTelegraph Spawn(Vector3 center, float radius, Color color, float duration, Shape shape = Shape.Disc, bool growFill = true, Transform follow = null)
    {
        var go = new GameObject($"Telegraph_{shape}");
        go.layer = 2;
        GroundTelegraph telegraph = go.AddComponent<GroundTelegraph>();
        telegraph.Setup(center, radius, color, duration, shape, growFill, follow);
        return telegraph;
    }

    private void Setup(Vector3 center, float radius, Color tint, float seconds, Shape shape, bool grow, Transform target)
    {
        Radius = radius;
        color = tint;
        duration = Mathf.Max(0.05f, seconds);
        follow = target;
        growFill = grow;
        block = new MaterialPropertyBlock();
        transform.position = new Vector3(center.x, center.y + 0.06f, center.z);

        Mesh mesh = shape == Shape.Ring ? RingMesh() : shape == Shape.Cone ? ConeMesh(coneAngle) : DiscMesh();
        outlineRenderer = CreatePart("Area", mesh, radius, new Color(tint.r, tint.g, tint.b, tint.a * 0.35f));
        fillRenderer = CreatePart("Fill", shape == Shape.Cone ? mesh : DiscMesh(), grow ? 0.01f : radius, tint);
        fill = fillRenderer.transform;
        fill.localPosition = Vector3.up * 0.01f;
        if (shape == Shape.Ring) fillRenderer.enabled = grow;
        Destroy(gameObject, duration);
    }

    public void SetRadius(float radius)
    {
        Radius = radius;
        if (outlineRenderer != null) outlineRenderer.transform.localScale = new Vector3(radius, 1f, radius);
    }

    private MeshRenderer CreatePart(string partName, Mesh mesh, float radius, Color tint)
    {
        var part = new GameObject(partName);
        part.layer = 2;
        part.transform.SetParent(transform, false);
        part.transform.localScale = new Vector3(radius, 1f, radius);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = RuntimeAssets.Telegraph;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        block.SetColor(BaseColorId, tint);
        renderer.SetPropertyBlock(block);
        return renderer;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (follow != null)
        {
            Vector3 p = follow.position;
            transform.position = new Vector3(p.x, p.y + 0.06f, p.z);
            if (rotateWithFollow)
            {
                Vector3 f = follow.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(f.normalized);
            }
        }

        if (growFill && fill != null)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            fill.localScale = new Vector3(Radius * t, 1f, Radius * t);
        }
        else if (!growFill && fillRenderer != null)
        {
            float alpha = 1f - Mathf.Clamp01(elapsed / duration);
            block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, color.a * alpha));
            fillRenderer.SetPropertyBlock(block);
        }
    }

    private static Mesh DiscMesh()
    {
        if (discMesh != null) return discMesh;

        var vertices = new Vector3[Segments + 1];
        var triangles = new int[Segments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = (i + 1) % Segments + 1;
            triangles[i * 3 + 2] = i + 1;
        }

        discMesh = new Mesh { name = "TelegraphDisc", vertices = vertices, triangles = triangles };
        discMesh.RecalculateNormals();
        return discMesh;
    }

    private static Mesh ConeMesh(float angle)
    {
        int key = Mathf.RoundToInt(angle);
        if (coneMeshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        const int steps = 24;
        var vertices = new Vector3[steps + 2];
        var triangles = new int[steps * 3];
        vertices[0] = Vector3.zero;
        float half = key * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(half, -half, i / (float)steps);
            vertices[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        for (int i = 0; i < steps; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        var mesh = new Mesh { name = $"TelegraphCone{key}", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        coneMeshes[key] = mesh;
        return mesh;
    }

    private static Mesh RingMesh()
    {
        if (ringMesh != null) return ringMesh;

        const float inner = 0.88f;
        var vertices = new Vector3[Segments * 2];
        var triangles = new int[Segments * 6];
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir;
            int next = (i + 1) % Segments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = next * 2;
            triangles[t + 2] = i * 2 + 1;
            triangles[t + 3] = i * 2 + 1;
            triangles[t + 4] = next * 2;
            triangles[t + 5] = next * 2 + 1;
        }

        ringMesh = new Mesh { name = "TelegraphRing", vertices = vertices, triangles = triangles };
        ringMesh.RecalculateNormals();
        return ringMesh;
    }
}
