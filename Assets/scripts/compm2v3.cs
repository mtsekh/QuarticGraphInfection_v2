using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class compm2v3 : MonoBehaviour
{
    [Header("Input Fields")]
    public TMP_InputField inputN;
    public TMP_InputField inputA;

    [Header("Outputs")]
    public TMP_Text outputText;
    public TMP_Text timerText;

    [Header("Graph")]
    public Transform vertexParent;
    public Material healthyMat;
    public Material infectedMat;

    [Header("Auto Infection")]
    public Toggle autoInfectToggle;
    public float autoStepDelay = 1.0f;

    // -------------------- COMPUTING VISUALS --------------------
    [Header("Computing Visuals")]
    public GameObject computingObject;     // spinner / icon / etc.
    public MonoBehaviour rotationScript;   // rotation script on that object

    // Progress displayed on main thread (worker writes to volatile int)
    public int currentK = 0;
    private volatile int workerK = 0;

    private float computeStartTime;
    private bool computeRunning = false;
    private CancellationTokenSource cancelSource;

    private int[] savedStartSet;
    private int savedN;
    private int savedA;

    private vertex[] verts;
    private Renderer[] vertRenderers;

    private Coroutine autoRoutine;

    // Reused buffers for stepping (avoid allocations each step)
    private bool[] stepSnapshot;
    private int[] stepToInfect;
    private int stepToInfectCount;

    // =====================================================================
    // UPDATE
    // =====================================================================
    void Update()
    {
        // pull worker progress safely
        currentK = workerK;

        if (!computeRunning) return;

        float elapsed = Time.time - computeStartTime;
        int h = (int)(elapsed / 3600);
        int m = (int)((elapsed % 3600) / 60);
        int s = (int)(elapsed % 60);

        if (timerText)
            timerText.text = $"{h:00}:{m:00}:{s:00}   k={currentK}";
    }

    // =====================================================================
    // START COMPUTATION
    // =====================================================================
    public async void ComputeM2Button()
    {
        if (computeRunning) return;

        StopAutoInfection();

        if (!int.TryParse(inputN.text, out int n) ||
            !int.TryParse(inputA.text, out int a))
        {
            if (outputText) outputText.text = "Invalid input.";
            return;
        }

        computeRunning = true;
        computeStartTime = Time.time;
        workerK = 0;
        currentK = 0;

        SetComputingVisuals(true);

        if (timerText) timerText.text = "00:00:00   k=0";
        if (outputText) outputText.text = "Computing m2...";

        cancelSource = new CancellationTokenSource();
        var token = cancelSource.Token;

        (int m2, int[] startSet) result;

        try
        {
            // NOTE: no Unity API in worker thread
            result = await Task.Run(() => ComputeM2_Optimized(n, a, token), token);
        }
        catch (OperationCanceledException)
        {
            computeRunning = false;
            SetComputingVisuals(false);
            if (outputText) outputText.text = "Computation cancelled.";
            return;
        }
        catch (Exception e)
        {
            computeRunning = false;
            SetComputingVisuals(false);
            if (outputText) outputText.text = $"Error: {e.Message}";
            return;
        }

        computeRunning = false;
        SetComputingVisuals(false);

        if (token.IsCancellationRequested)
        {
            if (outputText) outputText.text = "Computation terminated.";
            return;
        }

        if (result.m2 < 0 || result.startSet == null)
        {
            if (outputText)
                outputText.text = $"n = {n}, a = {a}\nm2 = impossible";
            return;
        }

        savedN = n;
        savedA = a;
        savedStartSet = result.startSet;

        if (outputText)
        {
            outputText.text =
                $"n = {n}, a = {a}\n" +
                $"m2 = {result.m2}\n" +
                $"Start = [{string.Join(", ", savedStartSet)}]";
        }

        SetupGraphForStepping();
        InfectStartVertices();
    }

    public void TerminateComputation()
    {
        cancelSource?.Cancel();
        SetComputingVisuals(false);
        if (outputText) outputText.text = "Termination requested...";
    }

    // =====================================================================
    // COMPUTING VISUALS
    // =====================================================================
    void SetComputingVisuals(bool active)
    {
        if (computingObject != null)
        {
            var r = computingObject.GetComponent<Renderer>();
            if (r != null)
                r.enabled = active;
        }

        if (rotationScript != null)
            rotationScript.enabled = active;
    }

    // =====================================================================
    // AUTO INFECTION
    // =====================================================================
    public void InfectionStepButton()
    {
        if (verts == null || savedStartSet == null)
        {
            if (outputText) outputText.text = "Compute m₂ first!";
            return;
        }

        if (autoInfectToggle != null && autoInfectToggle.isOn)
        {
            if (autoRoutine == null)
                StartAutoInfection();
        }
        else
        {
            ExecuteOneInfectionStep();
        }
    }

    void StartAutoInfection()
    {
        StopAutoInfection();
        autoRoutine = StartCoroutine(AutoInfectRoutine());
    }

    void StopAutoInfection()
    {
        if (autoRoutine != null)
        {
            StopCoroutine(autoRoutine);
            autoRoutine = null;
        }
    }

    IEnumerator AutoInfectRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoStepDelay);

            bool changed = ExecuteOneInfectionStep();
            if (!changed)
            {
                autoRoutine = null;
                yield break;
            }
        }
    }

    // =====================================================================
    // GRAPH VISUALIZATION
    // =====================================================================
    void SetupGraphForStepping()
    {
        verts = vertexParent.GetComponentsInChildren<vertex>();
        int n = savedN;

        if (vertRenderers == null || vertRenderers.Length != verts.Length)
            vertRenderers = new Renderer[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.infected = false;

            var r = vertRenderers[i];
            if (r == null) r = vertRenderers[i] = v.GetComponent<Renderer>();
            if (r) r.material = healthyMat;

            int idx = v.index;

            int im1 = Mod(idx - 1, n);
            int ip1 = Mod(idx + 1, n);
            int imA = Mod(idx - savedA, n);
            int ipA = Mod(idx + savedA, n);

            int[] tmp = new int[4];
            int cnt = 0;

            AddUnique(tmp, ref cnt, im1);
            AddUnique(tmp, ref cnt, ip1);
            AddUnique(tmp, ref cnt, imA);
            AddUnique(tmp, ref cnt, ipA);

            int[] nbrs = new int[cnt];
            Array.Copy(tmp, nbrs, cnt);
            v.nbrs = nbrs;
        }

        if (stepSnapshot == null || stepSnapshot.Length != n) stepSnapshot = new bool[n];
        if (stepToInfect == null || stepToInfect.Length != n) stepToInfect = new int[n];
    }

    static void AddUnique(int[] tmp, ref int cnt, int x)
    {
        for (int i = 0; i < cnt; i++)
            if (tmp[i] == x) return;
        tmp[cnt++] = x;
    }

    void InfectStartVertices()
    {
        for (int i = 0; i < savedStartSet.Length; i++)
        {
            int s = savedStartSet[i];
            verts[s].infected = true;
            var r = vertRenderers[s];
            if (r) r.material = infectedMat;
        }
    }

    bool ExecuteOneInfectionStep()
    {
        int n = savedN;

        for (int i = 0; i < n; i++)
            stepSnapshot[i] = verts[i].infected;

        stepToInfectCount = 0;

        for (int i = 0; i < n; i++)
        {
            if (stepSnapshot[i]) continue;

            int count = 0;
            var nbrs = verts[i].nbrs;
            for (int j = 0; j < nbrs.Length; j++)
                if (stepSnapshot[nbrs[j]]) count++;

            if (count >= 2)
                stepToInfect[stepToInfectCount++] = i;
        }

        if (stepToInfectCount == 0)
            return false;

        for (int t = 0; t < stepToInfectCount; t++)
        {
            int idx = stepToInfect[t];
            verts[idx].infected = true;
            var r = vertRenderers[idx];
            if (r) r.material = infectedMat;
        }

        return true;
    }

    // =====================================================================
    // OPTIMIZED M2 COMPUTATION (pruning + symmetry + fast BFS)
    // =====================================================================
    (int, int[]) ComputeM2_Optimized(int n, int a, CancellationToken token)
    {
        int[][] nbrs = BuildAdjacency(n, a, token);
        if (nbrs == null) return (-1, null);

        // You had this bound; keep it, but you can also cap to small constants first.
        int maxK = Mathf.Min(n, (a + 3) / 2);

        // Reused buffers (no per-candidate allocations inside BFS)
        int[] infectedStamp = new int[n];
        int[] infNbrCount = new int[n];
        int[] inStartStamp = new int[n];   // membership test for start set
        int stamp = 1;

        // Reused BFS queue (ring buffer)
        int[] q = new int[n];

        // Reused combination buffer (avoid allocating int[] per set)
        // We will clone only when we find the answer.
        for (int k = 2; k <= maxK; k++)
        {
            if (token.IsCancellationRequested) return (-1, null);

            workerK = k;

            // set[0] always 0; remaining are increasing
            int[] set = new int[k];
            set[0] = 0;

            // comb represents positions 1..k-1 chosen from 1..n-1
            int r = k - 1;
            int[] comb = new int[r];
            for (int i = 0; i < r; i++) comb[i] = i + 1;

            while (true)
            {
                if (token.IsCancellationRequested) return (-1, null);

                // materialize set from comb into reusable buffer
                for (int i = 0; i < r; i++) set[i + 1] = comb[i];

                // reflection symmetry pruning (roughly halves sets)
                if (!PassesReflectionCanonical(set, n))
                    goto NextCombination;

                // cheap “can infection even start?” prune (kills tons of sets)
                if (!CanStartSpread_Prune(n, nbrs, set, inStartStamp, infNbrCount, ref stamp))
                    goto NextCombination;

                // full verification (fast BFS)
                if (IsContagious_FastBfs(n, nbrs, set, infectedStamp, infNbrCount, q, ref stamp, token))
                {
                    int[] answer = new int[k];
                    Array.Copy(set, answer, k);
                    return (k, answer);
                }

            NextCombination:
                // next combination (lexicographic)
                int idx = r - 1;
                while (idx >= 0 && comb[idx] == (n - 1) - (r - 1 - idx))
                    idx--;

                if (idx < 0) break;

                comb[idx]++;
                for (int j = idx + 1; j < r; j++)
                    comb[j] = comb[j - 1] + 1;
            }
        }

        return (-1, null);
    }

    // Reflection symmetry reduction:
    // Accept only if the set is "no larger" than its reflected version under x -> n-x (mod n).
    static bool PassesReflectionCanonical(int[] set, int n)
    {
        // set is sorted increasing with set[0]=0
        int minNonZero = set[1];
        int max = set[set.Length - 1];
        return minNonZero <= (n - max);
    }

    // Cheap prune:
    // If no vertex outside S has >=2 neighbors in S initially, infection cannot start.
    // Reuses infNbrCount and inStartStamp; uses stamp to avoid clearing inStartStamp.
    static bool CanStartSpread_Prune(
        int n,
        int[][] nbrs,
        int[] startSet,
        int[] inStartStamp,
        int[] infNbrCount,
        ref int stamp)
    {
        stamp++;

        // mark start membership
        for (int i = 0; i < startSet.Length; i++)
            inStartStamp[startSet[i]] = stamp;

        // reset counts (we only need a clear for this prune; still O(n),
        // but MUCH cheaper than running BFS for hopeless sets)
        Array.Clear(infNbrCount, 0, n);

        // count infected neighbors from S
        for (int i = 0; i < startSet.Length; i++)
        {
            int v = startSet[i];
            var ns = nbrs[v];
            for (int j = 0; j < ns.Length; j++)
                infNbrCount[ns[j]]++;
        }

        // does any non-start vertex have >=2?
        for (int x = 0; x < n; x++)
        {
            if (inStartStamp[x] == stamp) continue;
            if (infNbrCount[x] >= 2) return true;
        }

        return false;
    }

    // Fast BFS verification:
    // - uses stamp technique for infectedStamp
    // - uses a ring buffer queue (int[] q)
    // - reuses infNbrCount (cleared once per call)
    static bool IsContagious_FastBfs(
        int n,
        int[][] nbrs,
        int[] startSet,
        int[] infectedStamp,
        int[] infNbrCount,
        int[] q,
        ref int stamp,
        CancellationToken token)
    {
        stamp++;
        Array.Clear(infNbrCount, 0, n);

        int infected = 0;

        int qHead = 0;
        int qTail = 0;

        // init with start set
        for (int i = 0; i < startSet.Length; i++)
        {
            int s = startSet[i];
            infectedStamp[s] = stamp;
            q[qTail++] = s;
            infected++;
        }

        if (infected == n) return true;

        while (qHead < qTail)
        {
            if (token.IsCancellationRequested) return false;

            int v = q[qHead++];

            var ns = nbrs[v];
            for (int j = 0; j < ns.Length; j++)
            {
                int u = ns[j];

                if (infectedStamp[u] == stamp) continue;

                int c = ++infNbrCount[u];
                if (c >= 2)
                {
                    infectedStamp[u] = stamp;
                    q[qTail++] = u;
                    infected++;
                    if (infected == n) return true;
                }
            }
        }

        return false;
    }

    // =====================================================================
    // ADJACENCY
    // =====================================================================
    static int[][] BuildAdjacency(int n, int a, CancellationToken token)
    {
        int[][] nbrs = new int[n][];

        for (int i = 0; i < n; i++)
        {
            if (token.IsCancellationRequested) return null;

            int im1 = Mod(i - 1, n);
            int ip1 = Mod(i + 1, n);
            int imA = Mod(i - a, n);
            int ipA = Mod(i + a, n);

            int[] tmp = new int[4];
            int cnt = 0;

            AddUnique(tmp, ref cnt, im1);
            AddUnique(tmp, ref cnt, ip1);
            AddUnique(tmp, ref cnt, imA);
            AddUnique(tmp, ref cnt, ipA);

            int[] arr = new int[cnt];
            Array.Copy(tmp, arr, cnt);
            nbrs[i] = arr;
        }

        return nbrs;
    }

    static int Mod(int x, int m) => (x % m + m) % m;
}
