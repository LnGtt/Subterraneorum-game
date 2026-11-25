using UnityEngine;

[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [SerializeField] private float minIntensity = 1.5f;
    [SerializeField] private float maxIntensity = 3f;
    [SerializeField] private float speed = 4f;

    public static float GlobalMultiplier { get; set; } = 1f;

    private Light targetLight;
    private float noiseOffset;

    private void Awake()
    {
        targetLight = GetComponent<Light>();
        noiseOffset = Random.value * 100f;
    }

    private void Update()
    {
        float noise = Mathf.PerlinNoise(noiseOffset, Time.time * speed);
        targetLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, noise) * GlobalMultiplier;
    }
}
