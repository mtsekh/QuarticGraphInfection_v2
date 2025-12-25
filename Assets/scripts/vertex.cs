using System.Collections.Generic;
using UnityEngine;

public class vertex : MonoBehaviour
{
    public int index;
    public int[] nbrs;
    public bool infected = false;

    [Header("Highlight")]
    public bool highlighted = false;

    [Header("Materials")]
    public Material defaultMaterial;                 // assign in Inspector
    public List<Material> highlightMaterials;         // assign in Inspector

    private Renderer outlineRenderer;

    // index -> vertex
    private static Dictionary<int, vertex> byIndex = new Dictionary<int, vertex>();

    void Awake()
    {
        outlineRenderer = transform.GetChild(0).GetComponent<Renderer>();

        if (defaultMaterial != null)
            outlineRenderer.material = defaultMaterial;
    }

    // -------------------- CLICK / INFECTION LOGIC --------------------
    public void HighlightSelfAndUnhighlightedNeighbors()
    {
        EnsureCache();

        // Rule 1: cannot infect if already highlighted
        if (highlighted)
            return;

        if (highlightMaterials == null || highlightMaterials.Count == 0)
        {
            Debug.LogWarning("No highlight materials assigned on vertex.");
            return;
        }

        // Choose color ONCE
        Material chosenMat = highlightMaterials[Random.Range(0, highlightMaterials.Count)];

        // Highlight THIS vertex
        ApplyMaterial(chosenMat);
        highlighted = true;

        // Infect neighbors ONLY if not highlighted
        if (nbrs == null) return;

        for (int i = 0; i < nbrs.Length; i++)
        {
            int nbIndex = nbrs[i];

            if (byIndex.TryGetValue(nbIndex, out vertex nbVertex) && nbVertex != null)
            {
                if (!nbVertex.highlighted)
                {
                    nbVertex.ApplyMaterial(chosenMat);
                    nbVertex.highlighted = true;
                }
            }
        }
    }

    // -------------------- RESET --------------------
    public void ResetHighlight()
    {
        highlighted = false;

        if (outlineRenderer != null && defaultMaterial != null)
            outlineRenderer.material = defaultMaterial;
    }

    // -------------------- INTERNAL HELPERS --------------------
    void ApplyMaterial(Material mat)
    {
        if (outlineRenderer == null || mat == null) return;
        outlineRenderer.material = mat;
    }

    static void EnsureCache()
    {
        byIndex.Clear();
        vertex[] verts = Object.FindObjectsOfType<vertex>();

        for (int i = 0; i < verts.Length; i++)
            byIndex[verts[i].index] = verts[i];
    }
}
