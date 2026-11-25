using UnityEngine;

public static class PartyLeash
{
    public static Rect ViewportArea { get; set; } = Rect.MinMaxRect(0.04f, 0.22f, 0.96f, 0.86f);

    public static bool Enabled { get; set; } = true;

    private static Camera camera;

    private static Camera Cam
    {
        get
        {
            if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
            return camera;
        }
    }

    private static bool Active => Enabled && Cam != null && PlayerRegistry.AliveCount >= 2;

    public static Vector3 ConstrainVelocity(PlayerStatus self, Vector3 position, Vector3 velocity, float dt)
    {
        if (!Active || self == null || self.IsDead || dt <= 0f) return velocity;

        Vector3 current = Cam.WorldToViewportPoint(position);
        if (current.z <= 0f) return velocity;
        Vector3 next = Cam.WorldToViewportPoint(position + velocity * dt);
        Rect area = ViewportArea;

        if ((next.x > area.xMax && next.x > current.x) || (next.x < area.xMin && next.x < current.x)) velocity.x = 0f;
        if ((next.y > area.yMax && next.y > current.y) || (next.y < area.yMin && next.y < current.y)) velocity.z = 0f;
        return velocity;
    }

    public static Vector3 ClampDestination(PlayerStatus self, Vector3 from, Vector3 to)
    {
        if (!Active || self == null) return to;

        const int steps = 12;
        for (int i = steps; i >= 0; i--)
        {
            Vector3 point = Vector3.Lerp(from, to, i / (float)steps);
            if (Inside(point)) return point;
        }

        return from;
    }

    public static bool Inside(Vector3 position)
    {
        if (Cam == null) return true;
        Vector3 p = Cam.WorldToViewportPoint(position);
        return p.z > 0f && ViewportArea.Contains(new Vector2(p.x, p.y));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ViewportArea = Rect.MinMaxRect(0.04f, 0.22f, 0.96f, 0.86f);
        Enabled = true;
        camera = null;
    }
}
