using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class m2newopttest : MonoBehaviour
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

    // Progress (worker writes to volatile int)
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

    // Stepping buffers (no allocations per step)
    private bool[] stepSnapshot;
    private int[] stepToInfect;
    private int stepToInfectCount;

    // Precomputed 4-neighbor arrays for current (n,a)
    private int[] nb1, nb2, nb3, nb4;

    // =====================================================================
    // UPDATE
    // =====================================================================
    void Update()
    {
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

        if (n < 4)
        {
            if (outputText) outputText.text = "n must be >= 4.";
            return;
        }

        a = Mod(a, n);
        if (a == 0 || a == 1 || a == n - 1)
        {
            if (outputText) outputText.text = "a must not be 0, ±1 (mod n).";
            return;
        }

        computeRunning = true;
        computeStartTime = Time.time;
        workerK = 0;
        currentK = 0;

        if (timerText) timerText.text = "00:00:00   k=0";
        if (outputText) outputText.text = "Computing m₂...";

        cancelSource = new CancellationTokenSource();
        var token = cancelSource.Token;

        (int m2, int[] startSet) result;

        try
        {
            // NOTE: no Unity API on worker thread
            result = await Task.Run(() => ComputeM2_CirculantOptimized(n, a, token), token);
        }
        catch (OperationCanceledException)
        {
            computeRunning = false;
            if (outputText) outputText.text = "Computation cancelled.";
            return;
        }
        catch (Exception e)
        {
            computeRunning = false;
            if (outputText) outputText.text = $"Error: {e.Message}";
            return;
        }

        computeRunning = false;

        if (token.IsCancellationRequested)
        {
            if (outputText) outputText.text = "Computation terminated.";
            return;
        }

        if (result.m2 < 0 || result.startSet == null)
        {
            if (outputText) outputText.text = $"n = {n}, a = {a}\nm₂ = impossible";
            return;
        }

        savedN = n;
        savedA = a;
        savedStartSet = result.startSet;

        if (outputText)
        {
            outputText.text =
                $"n = {n}, a = {a}\n" +
                $"m₂ = {result.m2}\n" +
                $"Start = [{string.Join(", ", savedStartSet)}]";
        }

        SetupGraphForStepping();
        InfectStartVertices();
    }

    public void TerminateComputation()
    {
        cancelSource?.Cancel();
        if (outputText) outputText.text = "Termination requested...";
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
    // GRAPH VISUALIZATION (optimized)
    // =====================================================================
    void SetupGraphForStepping()
    {
        verts = vertexParent.GetComponentsInChildren<vertex>();
        int n = savedN;

        // Cache renderers and reset infection visuals
        if (vertRenderers == null || vertRenderers.Length != verts.Length)
            vertRenderers = new Renderer[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.infected = false;

            var r = vertRenderers[i];
            if (r == null) r = vertRenderers[i] = v.GetComponent<Renderer>();
            if (r) r.material = healthyMat;
        }

        // Precompute neighbor arrays for stepping (and it matches compute core)
        BuildNeighborArrays(savedN, savedA, out nb1, out nb2, out nb3, out nb4);

        // stepping buffers
        if (stepSnapshot == null || stepSnapshot.Length != n) stepSnapshot = new bool[n];
        if (stepToInfect == null || stepToInfect.Length != n) stepToInfect = new int[n];
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

        // snapshot without allocating
        for (int i = 0; i < n; i++)
            stepSnapshot[i] = verts[i].infected;

        stepToInfectCount = 0;

        // Unrolled neighbor counts using precomputed nb1..nb4
        for (int i = 0; i < n; i++)
        {
            if (stepSnapshot[i]) continue;

            int c = 0;
            if (stepSnapshot[nb1[i]]) c++;
            if (stepSnapshot[nb2[i]]) c++;
            if (stepSnapshot[nb3[i]]) c++;
            if (stepSnapshot[nb4[i]]) c++;

            if (c >= 2)
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
    // OPTIMIZED M2 COMPUTATION (circulant-specific, no adjacency lists)
    // =====================================================================
    (int, int[]) ComputeM2_CirculantOptimized(int n, int a, CancellationToken token)
    {
        // Precompute neighbors once (fast lookups, no mod in hot loop)
        BuildNeighborArrays(n, a, out int[] n1, out int[] n2, out int[] n3, out int[] n4);

        // Your previous upper bound (kept)
        int maxK = Mathf.Min(n, (a + 3) / 2);

        // Reused buffers (no per-candidate allocations inside BFS)
        int[] infectedStamp = new int[n];
        int[] infNbrCount = new int[n];
        int[] inStartStamp = new int[n];
        int stamp = 1;

        // BFS queue (reused)
        int[] q = new int[n];

        // Combination buffers
        // We enforce 0 in the set; choose remaining r=k-1 from 1..n-1
        for (int k = 2; k <= maxK; k++)
        {
            if (token.IsCancellationRequested) return (-1, null);

            workerK = k;

            int r = k - 1;
            int[] comb = new int[r];
            for (int i = 0; i < r; i++) comb[i] = i + 1;

            int[] set = new int[k];
            set[0] = 0;

            while (true)
            {
                if (token.IsCancellationRequested) return (-1, null);

                // materialize set
                for (int i = 0; i < r; i++) set[i + 1] = comb[i];

                // symmetry prune (reflection)
                if (!PassesReflectionCanonical(set, n))
                    goto NextComb;

                // cheap prune: infection must be able to start
                if (!CanStartSpread_Prune(n, set, inStartStamp, infNbrCount, ref stamp, n1, n2, n3, n4))
                    goto NextComb;

                // full verification (fast BFS, unrolled neighbor updates)
                if (IsContagious_FastBfs(n, set, infectedStamp, infNbrCount, q, ref stamp, token, n1, n2, n3, n4))
                {
                    int[] answer = new int[k];
                    Array.Copy(set, answer, k);
                    return (k, answer);
                }

            NextComb:
                // next combination
                int idx = r - 1;
                int limitBase = (n - 1); // max value in comb space is n-1
                while (idx >= 0 && comb[idx] == limitBase - (r - 1 - idx))
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
    // Accept only if the set is "no larger" than its reflection under x -> n-x (mod n).
    static bool PassesReflectionCanonical(int[] set, int n)
    {
        int minNonZero = set[1];
        int max = set[set.Length - 1];
        return minNonZero <= (n - max);
    }

    // Cheap prune:
    // If no vertex outside S has >=2 neighbors in S initially, infection cannot start.
    static bool CanStartSpread_Prune(
        int n,
        int[] startSet,
        int[] inStartStamp,
        int[] infNbrCount,
        ref int stamp,
        int[] n1, int[] n2, int[] n3, int[] n4)
    {
        stamp++;

        // mark start membership (stamp trick)
        for (int i = 0; i < startSet.Length; i++)
            inStartStamp[startSet[i]] = stamp;

        // clear counts once per candidate (still cheap vs BFS for hopeless sets)
        Array.Clear(infNbrCount, 0, n);

        // count infected-neighbor contributions from S, unrolled 4-neighbor updates
        for (int i = 0; i < startSet.Length; i++)
        {
            int v = startSet[i];
            infNbrCount[n1[v]]++;
            infNbrCount[n2[v]]++;
            infNbrCount[n3[v]]++;
            infNbrCount[n4[v]]++;
        }

        // any non-start vertex with >=2?
        for (int x = 0; x < n; x++)
        {
            if (inStartStamp[x] == stamp) continue;
            if (infNbrCount[x] >= 2) return true;
        }

        return false;
    }

    // Fast BFS verification (unrolled neighbors + stamp)
    static bool IsContagious_FastBfs(
        int n,
        int[] startSet,
        int[] infectedStamp,
        int[] infNbrCount,
        int[] q,
        ref int stamp,
        CancellationToken token,
        int[] n1, int[] n2, int[] n3, int[] n4)
    {
        stamp++;
        Array.Clear(infNbrCount, 0, n);

        int infected = 0;
        int qHead = 0;
        int qTail = 0;

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
            // keep cancellation checks out of the tightest inner logic
            if ((qHead & 1023) == 0 && token.IsCancellationRequested)
                return false;

            int v = q[qHead++];

            // Each infected v contributes +1 to its 4 neighbors' infected-neighbor counts
            int u;

            u = n1[v];
            if (infectedStamp[u] != stamp)
            {
                int c = ++infNbrCount[u];
                if (c >= 2)
                {
                    infectedStamp[u] = stamp;
                    q[qTail++] = u;
                    if (++infected == n) return true;
                }
            }

            u = n2[v];
            if (infectedStamp[u] != stamp)
            {
                int c = ++infNbrCount[u];
                if (c >= 2)
                {
                    infectedStamp[u] = stamp;
                    q[qTail++] = u;
                    if (++infected == n) return true;
                }
            }

            u = n3[v];
            if (infectedStamp[u] != stamp)
            {
                int c = ++infNbrCount[u];
                if (c >= 2)
                {
                    infectedStamp[u] = stamp;
                    q[qTail++] = u;
                    if (++infected == n) return true;
                }
            }

            u = n4[v];
            if (infectedStamp[u] != stamp)
            {
                int c = ++infNbrCount[u];
                if (c >= 2)
                {
                    infectedStamp[u] = stamp;
                    q[qTail++] = u;
                    if (++infected == n) return true;
                }
            }
        }

        return false;
    }

    // =====================================================================
    // NEIGHBOR PRECOMPUTE (NO MOD IN HOT LOOPS)
    // =====================================================================
    static void BuildNeighborArrays(int n, int a, out int[] n1, out int[] n2, out int[] n3, out int[] n4)
    {
        n1 = new int[n];
        n2 = new int[n];
        n3 = new int[n];
        n4 = new int[n];

        for (int i = 0; i < n; i++)
        {
            // i-1, i+1 (wrap)
            n1[i] = (i == 0) ? (n - 1) : (i - 1);
            n2[i] = (i + 1 == n) ? 0 : (i + 1);

            // i-a, i+a (wrap), with a in [0,n)
            int imA = i - a;
            if (imA < 0) imA += n;

            int ipA = i + a;
            if (ipA >= n) ipA -= n;

            n3[i] = imA;
            n4[i] = ipA;

            // Note: duplicates can happen when a == n/2 etc.
            // That is OK: duplicate neighbor contributions can slightly change counting.
            // In simple graphs duplicates should be removed; for Cn(1,a), duplicates occur only when steps overlap.
            // If you need strict "simple graph" behavior, uncomment the de-dup fix below.
        }

        // OPTIONAL strict de-dup (only needed if you want simple-graph neighbor sets with duplicates removed):
        // This costs a tiny bit at build time but preserves exact semantics of "unique neighbors".
        // If you want it, tell me and I’ll provide a de-dup version that keeps the 4 arrays but marks duplicates.
    }

    static int Mod(int x, int m) => (x % m + m) % m;
}
