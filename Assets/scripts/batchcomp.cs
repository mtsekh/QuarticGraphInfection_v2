using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

public class BatchM2TableGenerator : MonoBehaviour
{
    [Header("Inputs")]
    public TMP_InputField inputMaxN;

    [Header("UI Feedback")]
    public TMP_Text statusText;
    public TMP_Text outputText;

    // ============================= BUTTON =============================
    public void GenerateTableButton()
    {
        if (!int.TryParse(inputMaxN.text, out int maxN) || maxN < 1)
        {
            outputText.text = "Invalid max n.";
            return;
        }

        outputText.text = "";
        statusText.text = "Starting computation...";

        // Auto-generate CSV file path on Desktop
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string fileName = "DATA.csv";
        string fullPath = Path.Combine(desktop, fileName);

        string table = ComputeTable(maxN);

        try
        {
            File.WriteAllText(fullPath, table);
        }
        catch (Exception e)
        {
            outputText.text = $"File error:\n{e.Message}";
            return;
        }

        statusText.text = "";
        outputText.text = $"DONE!\nSaved to Desktop:\n{fileName}";
    }

    // ============================= TABLE =============================
    string ComputeTable(int maxN)
    {
        StringBuilder sb = new StringBuilder();

        // Header row
        sb.Append("a/n");
        for (int n = 1; n <= maxN; n++)
            sb.Append($",{n}");
        sb.AppendLine();

        // Rows = a
        for (int a = 1; a <= maxN / 2; a++)
        {
            sb.Append(a);

            for (int n = 1; n <= maxN; n++)
            {
                if (a > n / 2)
                {
                    sb.Append(",=");
                    continue;
                }

                statusText.text = $"Computing m₂ for n={n}, a={a}";
                int m2 = ComputeM2(n, a);
                sb.Append($",{m2}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ============================= M2 =============================
    int ComputeM2(int n, int a)
    {
        if (n < 2) return -1;

        List<int>[] nbrs = new List<int>[n];
        for (int i = 0; i < n; i++)
            nbrs[i] = new List<int>();

        for (int i = 0; i < n; i++)
        {
            AddUnique(nbrs[i], (i + 1) % n);
            AddUnique(nbrs[i], (i - 1 + n) % n);
            AddUnique(nbrs[i], (i + a) % n);
            AddUnique(nbrs[i], (i - a + n) % n);
        }

        int maxK = Mathf.Min(n, (a + 3) / 2);

        for (int k = 2; k <= maxK; k++)
        {
            foreach (var set in GenerateRotationReducedSets(n, k))
            {
                bool[] infected = new bool[n];
                foreach (int s in set)
                    infected[s] = true;

                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (infected[i]) continue;

                        int count = 0;
                        foreach (int nb in nbrs[i])
                            if (infected[nb]) count++;

                        if (count >= 2)
                        {
                            infected[i] = true;
                            changed = true;
                        }
                    }
                }

                bool full = true;
                for (int i = 0; i < n; i++)
                    if (!infected[i]) { full = false; break; }

                if (full) return k;
            }
        }

        return -1;
    }

    // ============================= HELPERS =============================
    void AddUnique(List<int> list, int x)
    {
        if (!list.Contains(x))
            list.Add(x);
    }

    IEnumerable<int[]> GenerateRotationReducedSets(int n, int k)
    {
        int[] cur = new int[k];
        cur[0] = 0;

        IEnumerable<int[]> DFS(int depth, int start)
        {
            if (depth == k)
            {
                int[] res = new int[k];
                Array.Copy(cur, res, k);
                yield return res;
                yield break;
            }

            for (int i = start; i < n; i++)
            {
                cur[depth] = i;
                foreach (var r in DFS(depth + 1, i + 1))
                    yield return r;
            }
        }

        foreach (var r in DFS(1, 1))
            yield return r;
    }
}
