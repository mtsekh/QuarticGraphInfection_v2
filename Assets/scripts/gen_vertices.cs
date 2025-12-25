using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class gen_vertices : MonoBehaviour
{
    [Header("UI Input Fields")]
    public TMP_InputField inputN;
    public TMP_InputField inputA;

    [Header("Prefab")]
    public GameObject vertexPrefab;
    public float sizemod;

    [Header("Parent for spawned vertices")]
    public Transform vertexParent;   // <-- assign in inspector

    private Vector2 center = Vector2.zero;

    public int GetAValue()
    {
        return int.Parse(inputA.text);
    }

    public void GenerateVertices()
    {
        // Remove old vertices inside parent only
        if (vertexParent != null)
        {
            for (int i = vertexParent.childCount - 1; i >= 0; i--)
                Destroy(vertexParent.GetChild(i).gameObject);
        }
        else
        {
            Debug.LogError("❗ vertexParent not assigned in inspector");
            return;
        }

        // Parse inputs
        if (!int.TryParse(inputN.text.Trim(), out int n) || n < 1) return;
        if (!int.TryParse(inputA.text.Trim(), out int a) || a < 1) return;

        float radius = Mathf.Sqrt(n) * 0.5f;
        float optimalSize = Mathf.Clamp((0.8f / Mathf.Sqrt(n)) * sizemod, 0.01f, 1f);
        float angleStep = 360f / n;

        vertex[] allVertices = new vertex[n];

        // ───────────────────────── Generate vertices ─────────────────────────
        for (int i = 0; i < n; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;

            Vector2 pos = center + new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius
            );

            GameObject vObj = Instantiate(vertexPrefab, pos, Quaternion.identity, vertexParent);
            vObj.tag = "generated";
            vObj.transform.localScale = new Vector3(optimalSize, optimalSize, 1f);

            vertex vData = vObj.GetComponent<vertex>();
            vData.index = i;
            allVertices[i] = vData;
        }

        // Assign neighbors (±1, ±a) WITHOUT duplicates
        for (int i = 0; i < n; i++)
        {
            int im1 = Mod(i - 1, n);
            int ip1 = Mod(i + 1, n);
            int imA = Mod(i - a, n);
            int ipA = Mod(i + a, n);

            List<int> neigh = new List<int>();

            void AddUnique(int idx)
            {
                if (!neigh.Contains(idx))
                    neigh.Add(idx);
            }

            AddUnique(im1);
            AddUnique(ip1);
            AddUnique(imA);
            AddUnique(ipA);

            allVertices[i].nbrs = neigh.ToArray();
        }

        // Draw edges
        gen_Lines drawer = GetComponent<gen_Lines>();
        if (drawer != null)
            drawer.BuildGraph(allVertices);
    }

    private int Mod(int x, int m) => (x % m + m) % m;
}
