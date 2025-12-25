using System.Collections.Generic;
using UnityEngine;

public class gen_Lines : MonoBehaviour
{
    [Header("Line Settings")]
    public Material lineMaterial;      // assign a simple Unlit/Color material in the Inspector
    public float lineWidth = 0.03f;

    // Optionally keep references if you ever want to modify later
    private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();

    public void BuildGraph(vertex[] all)
    {
        // Clear previously created line objects (children of this object)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
        lineRenderers.Clear();

        if (all == null || all.Length == 0) return;

        // Use HashSet to ensure each undirected edge is only drawn once
        HashSet<(int, int)> usedEdges = new HashSet<(int, int)>();

        for (int i = 0; i < all.Length; i++)
        {
            foreach (int nb in all[i].nbrs)
            {
                int a = Mathf.Min(i, nb);
                int b = Mathf.Max(i, nb);
                var key = (a, b);

                if (!usedEdges.Add(key))
                    continue; // edge already drawn

                // Create a new child GameObject for this edge
                GameObject edgeObj = new GameObject($"edge_{a}_{b}");
                edgeObj.transform.SetParent(this.transform, worldPositionStays: false);

                LineRenderer lr = edgeObj.AddComponent<LineRenderer>();

                // Basic line settings
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;

                // Assign material if provided
                if (lineMaterial != null)
                    lr.material = lineMaterial;

                // For 2D, keep everything on same Z and in front
                lr.sortingLayerName = "Default";
                lr.sortingOrder = 0;

                // Set the positions
                Vector3 p1 = all[a].transform.position;
                Vector3 p2 = all[b].transform.position;

                lr.SetPosition(0, p1);
                lr.SetPosition(1, p2);

                lineRenderers.Add(lr);
            }
        }
    }
}
