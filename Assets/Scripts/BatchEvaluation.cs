using System;
using System.Collections.Generic;
using PathCreation.Examples;
using UnityEngine;

enum BatchMode
{
    CollectData, // collect data, normal mode, X, Z, XZ, or R mode
    FixedXZLoopRs, // use different Rs to collect data, XZ+R mode
    FixedXZRLoopDw // fine-tune weight, XZ+fixed R mode
}

public class BatchEvaluation : MonoBehaviour
{
    #region Public Fields

    [Header("Reference")] public PathFollower pathFollower;
    public RandomizedEvaluation randomizedEvaluation;

    [Header("Weights")] public float distanceWeight = 1.0f;
    public float rotationWeight = 1.0f;

    [Header("Threshold")] public float threshold = 1.0f;

    [Header("Batch - XZ")] public Boolean synchronizeXZ;

    [Header("Batch - X STD")] public Boolean XEnable;
    public Boolean useXRange;
    public float XMin;
    public float XMax;
    public float XStep;
    public List<float> XStds;

    [Header("Batch - Z STD")] public Boolean ZEnable;
    public Boolean useZRange;
    public float ZMin;
    public float ZMax;
    public float ZStep;
    public List<float> ZStds;

    [Header("Batch - R STD")] public Boolean REnable;
    public Boolean useRRange;
    public float RMin;
    public float RMax;
    public float RStep;
    public List<float> RStds;

    private int loopCount = 1;
    private Boolean tuneMode = false;
    private float tuningR = 1;

    // inform to start a new line
    public event System.Action newLine;

    #endregion

    #region Private Fields

    private Boolean loopDone;

    private Queue<float> XQueue;
    private Queue<float> ZQueue;
    private Queue<float> RQueue;

    private BatchMode batchMode;
    private Queue<float> WDQueue; // weight of distance queue


    #endregion

    // Start is called before the first frame update
    void Start()
    {
        if (pathFollower != null)
        {
            pathFollower.travelRestarted += OnTravelRestarted;
        }
        
        // set threshold
        randomizedEvaluation.SetThreshold(threshold);

        // Update enables
        randomizedEvaluation.UpdateEnables(XEnable, ZEnable, REnable);
        // Update weights
        randomizedEvaluation.UpdateWeights(distanceWeight, rotationWeight);
        // prepare all parameters
        SetAllXs();
        SetAllZs();
        SetAllRs();
        loopDone = false;
    }

    // Update is called once per frame
    void Update()
    {

    }

    #region Actions

    void OnTravelRestarted()
    {
        Debug.Log("Path restarted.");
        if (batchMode == BatchMode.FixedXZLoopRs)
        {
            UpdateParameter(tuningR);
            if (loopDone)
            {
                if (RQueue.Count > 0)
                {
                    SetAllXs();
                    SetAllZs();
                    loopDone = false;
                    tuningR = RQueue.Dequeue();
                    UpdateParameter(tuningR);
                    newLine();
                }
                else
                {
                    randomizedEvaluation.StopMoving();
                }
            }
            else
            {
                newLine();
            }
        }
        else if (batchMode == BatchMode.FixedXZRLoopDw)
        {
            UpdateParameter(stopIfDone: false);
            if (loopDone)
            {
                if (WDQueue.Count > 0)
                {
                    SetAllXs();
                    SetAllZs();
                    loopDone = false;
                    var wd = WDQueue.Dequeue();
                    UpdateWeights(wd, 0);
                    UpdateParameter(stopIfDone: false);
                    newLine();
                }
                else
                {
                    randomizedEvaluation.StopMoving();
                }
            }
            else
            {
                newLine();
            }
        }
        else
        {
            // Update parameters
            UpdateParameter();
            if (!loopDone)
            {
                newLine();
            }
        }
    }

    #endregion

    #region Batch Parameters

    /// <summary>
    /// Get all X parameters
    /// </summary>
    /// <returns></returns>
    void SetAllXs()
    {
        XQueue = new Queue<float>();
        if (synchronizeXZ)
        {
            ZQueue = new Queue<float>();
        }

        if (useXRange)
        {
            var s = XMin;
            var e = XMax;
            var step = XStep;
            while (s <= e)
            {
                XQueue.Enqueue(s);
                if (synchronizeXZ)
                {
                    ZQueue.Enqueue(s);
                }

                s += step;
            }
        }
        else
        {
            if (XStds != null)
            {
                foreach (var v in XStds)
                {
                    XQueue.Enqueue(v);
                    if (synchronizeXZ)
                    {
                        ZQueue.Enqueue(v);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get all Z parameters
    /// </summary>
    /// <returns></returns>
    void SetAllZs()
    {
        if (synchronizeXZ) return;
        ZQueue = new Queue<float>();
        if (useZRange)
        {
            var s = ZMin;
            var e = ZMax;
            var step = ZStep;
            while (s <= e)
            {
                ZQueue.Enqueue(s);
                s += step;
            }
        }
        else
        {
            if (ZStds != null)
            {
                foreach (var v in ZStds)
                {
                    ZQueue.Enqueue(v);
                }
            }
        }
    }

    /// <summary>
    /// Get all R parameters
    /// </summary>
    /// <returns></returns>
    void SetAllRs()
    {
        RQueue = new Queue<float>();
        if (useRRange)
        {
            RStds = new List<float>();
            var s = RMin;
            var e = RMax;
            var step = RStep;
            while (s <= e)
            {
                RQueue.Enqueue(s);
                s += step;
            }
        }
        else
        {
            if (RStds != null)
            {
                foreach (var v in RStds)
                {
                    RQueue.Enqueue(v);
                }
            }
        }
    }

    /// <summary>
    /// Prefix Rs for tune
    /// </summary>
    void PrefixRs()
    {
        RQueue = new Queue<float>();
        for (int i = 0; i < 10; i++)
        {
            RQueue.Enqueue(i + 1);
        }

        for (int i = 1; i < 36; i++)
        {
            RQueue.Enqueue((i + 1) * 10);
        }
    }

    void PrefixDWeights()
    {
        WDQueue = new Queue<float>();
        for (int i = 1; i < 100; i++)
        {
            WDQueue.Enqueue(i + 1);
        }
    }

    /// <summary>
    /// Update one parameter for noise generation
    /// </summary>
    /// <param name="stopIfDone">This is for not CollectData mode</param>
    void UpdateParameter(Boolean stopIfDone = true)
    {
        float x = 0;
        float z = 0;
        float r = 0;

        if (synchronizeXZ)
        {
            if (XQueue.Count > 0 && ZQueue.Count > 0)
            {
                x = XQueue.Dequeue();
                z = ZQueue.Dequeue();
            }
            else if (RQueue.Count > 0)
            {
                r = RQueue.Dequeue();
            }
            else
            {
                // all is looped
                Debug.LogWarning("Batch loop is done.");
                loopDone = true;
                if (stopIfDone)
                {
                    randomizedEvaluation.StopMoving();
                }

                return;
            }
        }
        else
        {
            if (XQueue.Count > 0)
            {
                x = XQueue.Dequeue();
            }
            else if (ZQueue.Count > 0)
            {
                z = ZQueue.Dequeue();
            }
            else if (RQueue.Count > 0)
            {
                r = RQueue.Dequeue();
            }
            else
            {
                // all is looped
                Debug.LogWarning("Batch loop is done.");
                loopDone = true;
                if (stopIfDone)
                {
                    randomizedEvaluation.StopMoving();
                }

                return;
            }
        }

        randomizedEvaluation.UpdateStds(x, z, r);
    }

    /// <summary>
    /// Update parameters with pre-fixed r
    /// </summary>
    /// <param name="r"></param>
    void UpdateParameter(float r)
    {
        float x = 0;
        float z = 0;

        if (synchronizeXZ)
        {
            if (XQueue.Count > 0 && ZQueue.Count > 0)
            {
                x = XQueue.Dequeue();
                z = ZQueue.Dequeue();
            }
            else
            {
                loopDone = true;
                return;
            }
        }
        else
        {
            if (XQueue.Count > 0)
            {
                x = XQueue.Dequeue();
            }
            else if (ZQueue.Count > 0)
            {
                z = ZQueue.Dequeue();
            }
            else
            {
                loopDone = true;
                return;
            }
        }

        randomizedEvaluation.UpdateStds(x, z, r);
    }

    /// <summary>
    /// Update weights
    /// </summary>
    /// <param name="wd"></param>
    /// <param name="wr"></param>
    void UpdateWeights(float wd, float wr)
    {
        randomizedEvaluation.UpdateWeights(wd, wr);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        if (GUI.Button(new Rect(10, 500, 100, 30), "Batch"))
        {
            // only loop X, Z, XZ or R
            batchMode = BatchMode.CollectData;
            loopDone = false;
            SetAllRs();
            // Update parameters
            UpdateParameter();
            // call to prepare
            newLine();
            // start
            randomizedEvaluation.StartMoving();
        }
        
        if (GUI.Button(new Rect(150, 500, 100, 30), "XZR"))
        {
            // fixed xz and loop r to visualize the result
            batchMode = BatchMode.FixedXZLoopRs;
            loopDone = false;
            SetAllRs();
            tuningR = RQueue.Dequeue();
            UpdateParameter(tuningR);
            newLine();
            // start
            randomizedEvaluation.StartMoving();
        }

        // if (GUI.Button(new Rect(150, 500, 100, 30), "Tune"))
        // {
        //     // fixed xz and loop r to visualize the result
        //     batchMode = BatchMode.FixedXZLoopRs;
        //     loopDone = false;
        //     PrefixRs();
        //     tuningR = RQueue.Dequeue();
        //     UpdateParameter(tuningR);
        //     newLine();
        //     // start
        //     randomizedEvaluation.StartMoving();
        // }

        // if (GUI.Button(new Rect(300, 500, 100, 30), "Weight"))
        // {
        //     // fixed xz and pre-defined r(120) and loop distance weight(1,100) to get optimal distance weight
        //     batchMode = BatchMode.FixedXZRLoopDw;
        //     loopDone = false;
        //     PrefixDWeights();
        //     UpdateParameter(stopIfDone: false);
        //     newLine();
        //     // start
        //     randomizedEvaluation.StartMoving();
        // }
    }

    #endregion
}