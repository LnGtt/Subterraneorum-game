using UnityEngine;

public static class RuntimeAssets
{
    private static Material telegraph;
    private static Material glow;
    private static Material spark;
    private static bool sparkMissing;

    public static Material Telegraph => telegraph != null ? telegraph : telegraph = Load("Subterraneorum/TelegraphMaterial");

    public static Material Glow => glow != null ? glow : glow = Load("Subterraneorum/GlowMaterial");

    public static Material Spark
    {
        get
        {
            if (spark != null || sparkMissing) return spark;
            spark = Load("Subterraneorum/SparkMaterial", "Configurar Atmosfera e Tochas");
            sparkMissing = spark == null;
            return spark;
        }
    }

    private static Material Load(string path, string builderMenu = "Configurar Fases 2 a 7")
    {
        Material material = Resources.Load<Material>(path);
        if (material == null)
        {
            Debug.LogWarning($"[RuntimeAssets] Material '{path}' não encontrado. Rode Subterraneorum › {builderMenu}.");
        }

        return material;
    }
}
