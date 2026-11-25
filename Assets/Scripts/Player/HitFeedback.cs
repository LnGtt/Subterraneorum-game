using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HitFeedback : MonoBehaviour
{
    [Tooltip("Opcional. Se vazio, todos os renderers 3D filhos piscam.")]
    [SerializeField] private Renderer characterRenderer;
    [SerializeField] private Material flashMaterial;
    [SerializeField] private float flashDuration = 0.1f;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Material[]> originalMaterials = new List<Material[]>();
    private Coroutine flashRoutine;

    private void Awake()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                renderers.Add(r);
                originalMaterials.Add(r.sharedMaterials);
            }
        }

        if (renderers.Count == 0 && characterRenderer != null)
        {
            renderers.Add(characterRenderer);
            originalMaterials.Add(characterRenderer.sharedMaterials);
        }
    }

    public void TriggerFlash()
    {
        if (flashMaterial == null || renderers.Count == 0 || !isActiveAndEnabled)
        {
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            Restore();
        }

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null) continue;
            Material[] flash = new Material[originalMaterials[i].Length];
            for (int m = 0; m < flash.Length; m++) flash[m] = flashMaterial;
            renderers[i].sharedMaterials = flash;
        }

        yield return new WaitForSeconds(flashDuration);
        Restore();
        flashRoutine = null;
    }

    private void Restore()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].sharedMaterials = originalMaterials[i];
            }
        }
    }

    private void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        Restore();
    }
}
