using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PathCreation.Examples;
using UnityEngine;
using Random = System.Random;
using MathNet.Numerics.Distributions;

public class RandomizedEvaluation : MonoBehaviour
{

    #region Public Fields

    [Header("Reference")] public MapGeneratorPreview mapGeneratorPreview;

    public PoseEstimation poseEstimation;

    [Header("Configuration")] [SerializeField] [Tooltip("Root to attach randomized objects")]
    private GameObject randomizedRoot;

    public Boolean enablePosition = true;
    public Boolean enableRotation = true;

    [SerializeField] [Tooltip("Color of the normal object")]
    private Color normalColor = Color.white;

    [SerializeField] [Tooltip("Color of the picked up object")]
    private Color pickedColor = Color.red;

    [Header("Path Follower")] public PathFollower pathFollower;
    [SerializeField] [Tooltip("Speed")] private float speed = 2.0f;

    // [Header("Detection")] [SerializeField] [Tooltip("How many objects can be detected in one time")]
    private int detectionCapacity = 1;

    [SerializeField] [Tooltip("Frequency in frames")]
    private float detectionFrequency = 100;

    [Tooltip("Continuously running (Whether to consider previous assignments)")] [SerializeField]
    private Boolean continuous = true;

    [Header("Gaussian Noise - Position")] public Boolean XNoise = true;
    public float XMean = 0.0f;
    public float XStdDev = 1.0f;

    public Boolean ZNoise = true;
    public float ZMean = 0.0f;
    public float ZStdDev = 1.0f;

    public Boolean boundaryConstraint = false;

    [Header("Gaussian Noise - Rotation")] public Boolean rotationNoise = true;
    public float rotationMean = 0.0f;
    public float rotationStdDev = 1.0f;
    public Boolean rotationConstraint = false;

    public System.Action accuracyAvailable;

    public System.Action layoutGenerated;

    #endregion

    #region Private Fields

    private GUIStyle guiStyleSmall = new GUIStyle();

    private Random _random;

    private Dictionary<GameObject, string> _randomizedObjects; // all noisy objects and their ground truth
    private List<GameObject> detected; // objects that are only within FOV

    private List<string> _GTLabels;

    private List<string> _assignedLabels;

    private List<GameObject> _pickedObjects;

    private Boolean started = false;
    private long frame = 0;

    private Normal XNormalDist;
    private Normal ZNormalDist;
    private Normal RNormalDist;

    #endregion

    // Start is called before the first frame update
    void Start()
    {
        _random = new Random();
        _randomizedObjects = new Dictionary<GameObject, string>();
        _GTLabels = new List<string>();
        _assignedLabels = new List<string>();

        _pickedObjects = new List<GameObject>();

        guiStyleSmall.fontSize = 20;
        guiStyleSmall.fontStyle = new FontStyle();
        guiStyleSmall.normal.textColor = new Color(1, 1, 1);

        XNormalDist = new Normal(XMean, XStdDev);
        ZNormalDist = new Normal(ZMean, ZStdDev);
        RNormalDist = new Normal(rotationMean, rotationStdDev);
        // stop moving at the beginning
        StopMoving();
    }

    // Update is called once per frame
    void Update()
    {
        if (started)
        {
            frame += 1;
            if (frame % detectionFrequency == 0)
            {
                // DynamicallyRandomize();
                LayoutBasedAssignment(PlayerTransform());
            }
        }
    }

    /// <summary>
    /// Reset all on-the-fly
    /// </summary>
    private void ResetAll()
    {
        ResetAssignment();
        frame = 0;
        StopMoving();
    }

    private void ResetAssignment()
    {
        _randomizedObjects.Clear();
        _GTLabels.Clear();
        _assignedLabels.Clear();
        _pickedObjects.Clear();

        poseEstimation.RemoveAllChildren(randomizedRoot);
    }

    private void OnGUI()
    {
        if (GUI.Button(new Rect(10, 300, 100, 30), "Noise"))
        {
            GenerateLayout();
        }

        if (GUI.Button(new Rect(150, 300, 100, 30), "Assign"))
        {
            AssignLabels(PlayerTransform());
            CollectLabelsAfterAssignment();
        }

        if (GUI.Button(new Rect(300, 300, 100, 30), "Start"))
        {
            StartMoving();
        }

        if (GUI.Button(new Rect(400, 300, 100, 30), "Stop"))
        {
            StopMoving();
        }

        if (GUI.Button(new Rect(10, 350, 100, 30), "Reset"))
        {
            ResetAll();
        }

        DrawText(SetupDebugText());
    }

    #region Path Follower

    /// <summary>
    /// Stop moving
    /// </summary>
    public void StopMoving()
    {
        started = false;
        pathFollower.speed = 0;
    }

    /// <summary>
    /// Start moving
    /// </summary>
    public void StartMoving()
    {
        started = true;
        pathFollower.speed = speed;
    }

    /// <summary>
    /// Randomly pick up objects and assign when the player is moving
    /// </summary>
    private void DynamicallyRandomize()
    {
        if (!continuous)
        {
            // clean previous assignments
            ResetAssignment();
        }

        for (int i = 0; i < detectionCapacity; i++)
        {
            NewRandomObject();
        }

        AssignLabels(PlayerTransform());
        CollectLabelsAfterAssignment();
    }

    Transform PlayerTransform()
    {
        return poseEstimation.player.transform;
    }

    #endregion

    #region Randomize

    /// <summary>
    /// Generate a random integer
    /// </summary>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    private int RandomInt(int min, int max)
    {
        return _random.Next(min, max);
    }

    /// <summary>
    /// Randomly pick up one object
    /// </summary>
    /// <param name="gameObjects"></param>
    /// <returns></returns>
    private GameObject RandomlyPickupOneObject(List<GameObject> gameObjects)
    {
        var size = gameObjects.Count;
        var index = RandomInt(0, size);
        return gameObjects[index];
    }

    /// <summary>
    /// Randomly pick up from one's children
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    private GameObject RandomlyPickupOneChild(GameObject go)
    {
        var count = go.transform.childCount;
        var index = RandomInt(0, count);
        return go.transform.GetChild(index).gameObject;
    }

    /// <summary>
    /// Generate a new random object
    /// </summary>
    private void NewRandomObject()
    {
        var objs = poseEstimation.GetObjectsOfWithinFOVSites();
        GameObject go = null;
        while (_pickedObjects.Count < objs.Count)
        {
            // pick up
            go = RandomlyPickupOneObject(objs);
            if (!_pickedObjects.Contains(go))
            {
                _pickedObjects.Add(go);
                break;
            }
        }

        if (go == null)
        {
            Debug.LogError("No more available objects for picking up.");
            return;
        }

        // clone
        var obj = poseEstimation.CloneObject(go, randomizedRoot, copyLabel: true);

        // ensure it's visible
        if (!obj.activeSelf) obj.SetActive(true);

        if (enablePosition)
        {
            // move, ensure the object is within FOV after moving
            RandomlyMove(obj);
            // while (!poseEstimation.IsPointWithinFOV(obj.transform.position))
            // {
            //     RandomlyMove(obj);
            // }
        }


        if (enableRotation)
        {
            // rotate
            RandomlyRotate(obj);
        }

        var label = poseEstimation.GetLabel(obj);

        // update label, remove first for assignment
        UpdateLabel(obj, "", pickedColor);

        _randomizedObjects.Add(obj, label);
    }

    /// <summary>
    /// Generate a new random position
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private Vector2 NextRandomPosition(Vector2 position)
    {
        var x = position.x;
        var y = position.y;

        var ratio = RandomInt(1, 20) / 10.0f;

        // 0: x, 1: y, 2: xy

        var idx = RandomInt(0, 2);

        if (idx == 0)
        {
            // move x
            x *= ratio;
        }
        else if (idx == 1)
        {
            // move y
            y *= ratio;
        }
        else
        {
            x *= ratio;
            y *= ratio;
        }

        // refine the cases when new position is out of the board
        var size = mapGeneratorPreview.meshSize;
        if (x > size) x = size;
        if (y > size) y = size;

        return new Vector2(x, y);
    }

    /// <summary>
    /// Wrapper for vector3
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private Vector3 NextRandomPosition(Vector3 position)
    {
        var pos = new Vector2(position.x, position.z);
        var result = NextRandomPosition(pos);
        return new Vector3(result.x, position.y, result.y);
    }


    /// <summary>
    /// Randomly move object
    /// </summary>
    /// <param name="go"></param>
    private void RandomlyMove(GameObject go)
    {
        var pos = go.transform.position;
        go.transform.position = NextRandomPosition(pos);
    }

    private void RandomlyRotate(GameObject go)
    {
        var degree = RandomInt(0, 360);
        go.transform.Rotate(Vector3.up, degree);
    }

    /// <summary>
    /// Update label's text and color
    /// </summary>
    /// <param name="go"></param>
    /// <param name="text"></param>
    /// <param name="color"></param>
    private void UpdateLabel(GameObject go, string text, Color color)
    {
        TextMesh textMesh = null;
        textMesh = go.GetComponentInChildren<TextMesh>();
        if (textMesh != null)
        {
            textMesh.color = color;
            textMesh.text = text;
        }
    }

    /// <summary>
    /// Get text for TextMesh
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    private string GetLabel(GameObject go)
    {
        TextMesh textMesh = null;
        textMesh = go.GetComponentInChildren<TextMesh>();
        if (textMesh != null)
        {
            return textMesh.text;
        }

        return "";
    }

    private void DrawText(string text)
    {
        GUI.Label(new Rect(10, 400, 200, 200), text, guiStyleSmall);
    }

    #endregion

    #region Assign Labels

    /// <summary>
    /// Assign labels
    /// </summary>
    private void AssignLabels(Transform transform)
    {
        var objs = _randomizedObjects.Keys.ToList();
        // loop and check result
        detected = poseEstimation.FilterObjectsWithinFOV(objs, transform);
        // run assignment
        poseEstimation.AssignObjects(detected);
    }

    /// <summary>
    /// Collect detected objects' labels after assignment
    /// </summary>
    private void CollectLabelsAfterAssignment()
    {
        _GTLabels.Clear();
        _assignedLabels.Clear();
        foreach (var obj in detected)
        {
            var gt = _randomizedObjects[obj];

            var assigned = GetLabel(obj);

            _GTLabels.Add(gt);
            _assignedLabels.Add(assigned);
        }
    }

    /// <summary>
    /// Update objects' colors for better visualization
    /// </summary>
    private void UpdateColorsAfterAssignment()
    {
        foreach (var obj in _randomizedObjects.Keys.ToList())
        {
            if (detected.Contains(obj))
            {
                UpdateLabel(obj, GetLabel(obj), pickedColor);
            }
            else
            {
                UpdateLabel(obj, "", normalColor);
            }
        }
    }

    /// <summary>
    /// Prepare debug text for visualization
    /// </summary>
    /// <returns></returns>
    private string SetupDebugText()
    {
        string text = $"Ground truth: {String.Join(",", _GTLabels)}\n" +
                      $"    Assigned: {String.Join(",", _assignedLabels)}\n" +
                      $"    Accuracy: {CountCorrect(_GTLabels, _assignedLabels)} / {_GTLabels.Count}";
        return text;
    }

    private int CountCorrect(List<string> gt, List<string> detected)
    {
        var count = 0;
        var size = gt.Count;
        for (var i = 0; i < size; i++)
        {
            if (gt[i] == detected[i])
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>
    /// Calculate accuracy
    /// </summary>
    /// <returns></returns>
    public float GetAccuracy()
    {
        // invalid 
        if (_GTLabels.Count == 0) return -1;
        return CountCorrect(_GTLabels, _assignedLabels) * 1.0f / _GTLabels.Count;
    }

    #endregion

    #region Layout Generation

    /// <summary>
    /// Generate new layout according to Gaussian Noise
    /// </summary>
    private void GenerateLayout()
    {
        // clear first
        ResetAssignment();
        var objects = poseEstimation.GetAllObjects();
        foreach (var obj in objects)
        {
            // clone first
            var cloned = poseEstimation.CloneObject(obj, randomizedRoot, copyLabel: true);

            // add ground truth
            var label = poseEstimation.GetLabel(cloned);
            // update label, remove first for assignment
            UpdateLabel(cloned, "", Color.white);
            _randomizedObjects.Add(cloned, label);

            // position noise
            AddPositionNoise(cloned);
            // rotation noise
            AddRotationNoise(cloned);
        }

        layoutGenerated();
    }

    /// <summary>
    /// Add position noise
    /// </summary>
    /// <param name="obj"></param>
    private void AddPositionNoise(GameObject obj)
    {
        Vector3 pos = Vector3.zero;
        var size = mapGeneratorPreview.meshSize;
        if (XNoise)
        {
            pos.x = (float)XNormalDist.Sample();
        }

        if (ZNoise)
        {
            pos.z = (float)ZNormalDist.Sample();
        }

        obj.transform.Translate(pos);

        if (boundaryConstraint)
        {
            var x = obj.transform.position.x;
            var y = obj.transform.position.y;
            var z = obj.transform.position.z;

            if (x < 0)
            {
                x = 0;
            }
            else if (x > size)
            {
                x = size;
            }

            if (z < 0)
            {
                z = 0;
            }
            else if (z > size)
            {
                z = size;
            }

            obj.transform.position = new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// Add rotation noise
    /// </summary>
    /// <param name="obj"></param>
    private void AddRotationNoise(GameObject obj)
    {
        if (rotationNoise)
        {
            var value = (float)RNormalDist.Sample();
            if (rotationConstraint)
            {
                if (value > 360)
                {
                    value = 360;
                }
                else if (value < -360)
                {
                    value = -360;
                }
            }

            obj.transform.Rotate(Vector3.up, value);
        }
    }

    /// <summary>
    /// Use current layout to assign labels
    /// </summary>
    private void LayoutBasedAssignment(Transform transform)
    {
        AssignLabels(transform);
        CollectLabelsAfterAssignment();
        UpdateColorsAfterAssignment();
        if (accuracyAvailable != null)
        {
            accuracyAvailable();   
        }
    }

    #endregion

    #region Getters

    /// <summary>
    /// Whether the simulation is running
    /// </summary>
    /// <returns></returns>
    public Boolean IsMoving()
    {
        return started;
    }


    #endregion

    #region Setters
    
    /// <summary>
    /// Set threshold for sites filtering
    /// </summary>
    /// <param name="v"></param>
    public void SetThreshold(float v)
    {
        poseEstimation.SetThreshold(v);
    }
    
    #endregion

    #region Update Normal Distributions

    /// <summary>
    /// Update stds for noise generation
    /// = 0 means to not update
    /// </summary>
    /// <param name="xstd"></param>
    /// <param name="zstd"></param>
    /// <param name="rstd"></param>
    public void UpdateStds(float xstd = 0, float zstd = 0, float rstd = 0)
    {
        if (xstd > 0)
        {
            XStdDev = xstd;
            XNormalDist = new Normal(XMean, XStdDev);
        }

        if (zstd > 0)
        {
            ZStdDev = zstd;
            ZNormalDist = new Normal(ZMean, ZStdDev);
        }

        if (rstd > 0)
        {
            rotationStdDev = rstd;
            RNormalDist = new Normal(rotationMean, rotationStdDev);
        }

        var pe = poseEstimation;

        Debug.LogError(
            $"XZR STD: ({XStdDev}, {ZStdDev}, {rotationStdDev}), DR Weights:({pe.distanceWeight}, {pe.rotationWeight})");
        // generate new layout
        GenerateLayout();
    }

    /// <summary>
    /// Update Noise Enable variables
    /// </summary>
    /// <param name="x"></param>
    /// <param name="z"></param>
    /// <param name="r"></param>
    public void UpdateEnables(Boolean x, Boolean z, Boolean r)
    {
        XNoise = x;
        ZNoise = z;
        rotationNoise = r;
    }

    /// <summary>
    /// Update costs weights
    /// </summary>
    /// <param name="distance"></param>
    /// <param name="rotation"></param>
    public void UpdateWeights(float distance, float rotation)
    {
        if (distance > 0)
        {
            poseEstimation.distanceWeight = distance;
        }

        if (rotation > 0)
        {
            poseEstimation.rotationWeight = rotation;
        }
    }

    #endregion
}
