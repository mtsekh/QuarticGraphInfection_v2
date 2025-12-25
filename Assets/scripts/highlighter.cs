using UnityEngine;

public class VertexRaycaster : MonoBehaviour
{
    [Header("Reference")]
    public Transform vertexParent;

    void Update()
    {
        if (Camera.main == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (hit.collider.CompareTag("generated"))
            {
                vertex v = hit.collider.GetComponent<vertex>();
                if (v != null)
                    v.HighlightSelfAndUnhighlightedNeighbors();
            }
        }
    }

    // -------------------- RESET (BUTTON) --------------------
    public void ResetAllHighlights()
    {
        if (vertexParent == null)
        {
            Debug.LogError("❗ VertexRaycaster: vertexParent not assigned");
            return;
        }

        for (int i = 0; i < vertexParent.childCount; i++)
        {
            vertex v = vertexParent.GetChild(i).GetComponent<vertex>();
            if (v != null)
                v.ResetHighlight();
        }
    }
}
