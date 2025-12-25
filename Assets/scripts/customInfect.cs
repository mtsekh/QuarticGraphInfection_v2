using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CustomInfectionController : MonoBehaviour
{
    [Header("Input")]
    public TMP_InputField infectedVerticesInput; // "0,1,2,3"

    [Header("Graph")]
    public Transform vertexParent;
    public Material healthyMat;
    public Material infectedMat;

    [Header("Auto Infection")]
    public Toggle autoInfectToggle;
    public float autoStepDelay = 1.0f;

    private Coroutine autoRoutine;

    // =====================================================================
    // BUTTON — INFECT / STEP
    // =====================================================================
    public void InfectButton()
    {
        StopAutoInfection();

        vertex[] verts = GetVertices();
        if (verts == null) return;

        // -------------------------------------------------------------
        // FIRST PRESS → initialize infection ONLY
        // -------------------------------------------------------------
        if (!AnyInfected(verts))
        {
            ResetGraph(verts);

            if (!ParseInput(infectedVerticesInput.text, verts.Length, out List<int> start))
                return;

            foreach (int v in start)
            {
                verts[v].infected = true;
                verts[v].GetComponent<Renderer>().material = infectedMat;
            }

            // ⛔ IMPORTANT: stop here (NO spreading yet)
            return;
        }

        // -------------------------------------------------------------
        // SUBSEQUENT PRESSES → spread infection
        // -------------------------------------------------------------
        if (autoInfectToggle != null && autoInfectToggle.isOn)
        {
            autoRoutine = StartCoroutine(AutoInfectRoutine());
        }
        else
        {
            ExecuteOneStep(verts);
        }
    }

    // =====================================================================
    // AUTO MODE
    // =====================================================================
    IEnumerator AutoInfectRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoStepDelay);

            vertex[] verts = GetVertices();
            if (verts == null) yield break;

            bool changed = ExecuteOneStep(verts);
            if (!changed)
            {
                autoRoutine = null;
                yield break;
            }
        }
    }

    void StopAutoInfection()
    {
        if (autoRoutine != null)
        {
            StopCoroutine(autoRoutine);
            autoRoutine = null;
        }
    }

    // =====================================================================
    // ONE SIMULTANEOUS INFECTION STEP
    // =====================================================================
    bool ExecuteOneStep(vertex[] verts)
    {
        int n = verts.Length;
        bool[] snapshot = new bool[n];

        for (int i = 0; i < n; i++)
            snapshot[i] = verts[i].infected;

        List<int> toInfect = new List<int>();

        for (int i = 0; i < n; i++)
        {
            if (snapshot[i]) continue;

            int count = 0;
            foreach (int nb in verts[i].nbrs)
                if (snapshot[nb]) count++;

            if (count >= 2)
                toInfect.Add(i);
        }

        if (toInfect.Count == 0)
            return false;

        foreach (int i in toInfect)
        {
            verts[i].infected = true;
            verts[i].GetComponent<Renderer>().material = infectedMat;
        }

        return true;
    }

    // =====================================================================
    // HELPERS
    // =====================================================================
    vertex[] GetVertices()
    {
        if (vertexParent == null) return null;

        vertex[] verts = vertexParent.GetComponentsInChildren<vertex>();
        if (verts.Length == 0) return null;

        return verts;
    }

    bool AnyInfected(vertex[] verts)
    {
        foreach (var v in verts)
            if (v.infected) return true;
        return false;
    }

    void ResetGraph(vertex[] verts)
    {
        foreach (var v in verts)
        {
            v.infected = false;
            v.GetComponent<Renderer>().material = healthyMat;
        }
    }

    bool ParseInput(string text, int n, out List<int> result)
    {
        result = new List<int>();
        text = text.Trim();

        if (string.IsNullOrEmpty(text))
            return false;

        string[] parts = text.Split(',');

        foreach (string p in parts)
        {
            if (!int.TryParse(p.Trim(), out int v))
                return false;

            if (v < 0 || v >= n)
                return false;

            if (!result.Contains(v))
                result.Add(v);
        }

        return result.Count > 0;
    }
}
