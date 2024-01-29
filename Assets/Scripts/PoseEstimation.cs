using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Delaunay.Geo;
// using JetBrains.Annotations;
// using TMPro;
// using UnityEditor.Experimental.TerrainAPI;
using UnityEngine;
using UnityEditor;
using LpSolveDotNet;

enum ControllingMode
{
    Object,
    Player,
    Distance,
    Rotation
}

public class PoseEstimation : MonoBehaviour
{
    #region Public Fields

    [Header("Map Generator")] public MapGeneratorPreview mapGeneratorPreview;

    [Header("Collider Manager")] public bool alwaysEnable = true; // Not working in play mode

    [Header("Collider Container")] public GameObject colliderContainer;

    [Header("Voronoi Graph")] public GameObject originalObjectsGameObject; // original objects (flattened)
    public GameObject graphRootGameObject; // the root game object to attach the result
    public GameObject sitesGameObject; // sites to generate voronoi graph
    public Dictionary<string, GameObject> siteNameGameObjectDic; // site game object associated with name

    [Header("Ground Truth")] public bool showGroundTruthLabel = true;
    public Color groundTruthLabelColor = Color.white;
    public bool ignoreInvisible = true;

    public Vector3 labelPosition = Vector3.zero;
    public Vector3 labelRotation = Vector3.zero;
    public Vector3 labelScale = Vector3.one;

    [Header("Player")] public GameObject player;
    public LineRenderer orientation;
    public int orientationLength = 5;
    public int FOVLength = 20;
    public float lineWidth = 0.1f;
    public LineRenderer fovLine1 = new LineRenderer();
    public LineRenderer fovLine2 = new LineRenderer();
    public bool updateOnPlay = true;
    public bool showOrientation = false;
    public bool showFOV = false;

    [Header("Mixed Integer Programming")] public Boolean normalizeCost = true;
    public GameObject templatesRoot;

    // for highlighting
    public Material selectedMaterial;

    public Color estimatedLabelColor = Color.red;

    public string saveModelName = "pose";


    // [Header("Probability")]
    // public float ratioWithinSite = 0.5f; // the ratio of the min distance of other sites not in this site

    [Header("Simulation")] public bool keyboardInput = true;
    public float increaseRatio = 1.1f;
    public float decreaseRatio = 0.9f;
    public GameObject detectedRoot;
    public Boolean rangeSimulation = true; // simulate the camera sensor range
    public float rangeMaximum = 3.0f;

    private float sigma1 = 0.6827f;
    private float sigma2 = 0.9545f;
    private float sigma3 = 0.9973f;

    [Header("Probability")] public bool useSoftmax = true;

    [Header("Cost Weights")]
    // weights for lp
    public float distanceWeight = 1.0f;

    public float rotationWeight = 1.0f;


    #endregion


    #region Private Fields

    // {name: Material}
    private Dictionary<string, Material> templateMaterialDict;
    private Dictionary<string, GameObject> templateDict;

    // use the setting of https://store.intelrealsense.com/buy-intel-realsense-depth-camera-d435.html
    // Depth Field of View (FOV)	87° × 58° (±3°)
    // RGB Sensor FOV	69° × 42°
    // Then, we take 69 = 35 + 34
    private int FOV1 = 35;
    private int FOV2 = 34;

    private GUIStyle guiStyleLarge = new GUIStyle(); //for OnGUI()
    private GUIStyle guiStyleSmall = new GUIStyle(); //for OnGUI()

    // match result 
    private string siteProbText = "";
    private string siteChairsProbText = "";
    private string objectsProbText = "";
    private string withinFOV = "";
    private string withinSigma = "";

    private string determinedOrder = "";

    // possible chairs
    private string possibles = "";

    // total cost
    private string totalCost = "";
    
    // cache objects' dimension cost to avoid unnecessary computing
    private Dictionary<int, Dictionary<string, float>>
        dimensionCostCache; // detected object index: {template name, cost}

    // sorted sites with probs
    private List<KeyValuePair<string, float>> sortedSitesWithProb;

    // threshold to split sites
    private float sitesProbThreshold = 1.0f;

    // detected chairs list
    private List<GameObject> detectedObjects = null;

    // cursor of the selected chair, this is for controlling to move or rotate
    private int detectedObjectCursor = -1;

    // controlling player or controlling the detected chairs
    // private bool controllingPlayer = true;

    // controlling mode
    private ControllingMode controllingMode = ControllingMode.Player;

    #endregion


    private bool controllingPlayer()
    {
        return controllingMode == ControllingMode.Player;
    }

    // Start is called before the first frame update
    void Start()
    {
        siteNameGameObjectDic = new Dictionary<string, GameObject>();

        RemoveAllChildren(colliderContainer);
        // set style
        guiStyleLarge.fontSize = 30;
        guiStyleLarge.fontStyle = new FontStyle();
        guiStyleLarge.normal.textColor = new Color(1, 1, 1);
        guiStyleSmall.fontSize = 20;
        guiStyleSmall.fontStyle = new FontStyle();
        guiStyleSmall.normal.textColor = new Color(1, 1, 1);

        // init templates
        InitializeTemplates(gameobject: true, material: true);
        // auto generate
        CreateVoronoiGraph();

        GetAllSitesDistances(player.transform.position);
        // show objects' label
        ShowGroundTruthLabel();
        // init detected chairs
        InitializeDetectedObjects();
        // at the beginning, controlling the player(camera)
        controllingMode = ControllingMode.Player;

        LpSolve.Init();
    }

    void DebugLPSolover()
    {
        double[] cost = new[]
        {
            22.0,
            30.0,
            26.0,
            16.0,
            25.0,
            27.0,
            29.0,
            28.0,
            20.0,
            32.0,
            33.0,
            25.0,
            21.0,
            29.0,
            23.0,
            24.0,
            24.0,
            30.0,
            19.0,
            26.0,
            30.0,
            33.0,
            32.0,
            37.0,
            31.0
        };
        LPAssignmentSolver(5, 5, cost);
        // this target cost should equal 118.
    }


    private void ResetLineRenders()
    {
        if (orientation != null) orientation.positionCount = 0;
        if (fovLine1 != null) fovLine1.positionCount = 0;
        if (fovLine2 != null) fovLine2.positionCount = 0;
        siteProbText = "";
        siteChairsProbText = "";
        objectsProbText = "";
        withinFOV = "";
        withinSigma = "";
        determinedOrder = "";
        possibles = "";
    }


    public void ShowOrientation()
    {
        if (showOrientation)
        {
            var p1 = player.transform.position;
            var p2 = p1 + player.transform.forward * orientationLength;
            DrawRay(orientation, p1, p2);
        }
        else
        {
            UnDrawRay(orientation);
        }
    }

    private void UnDrawRay(LineRenderer lineRenderer)
    {
        lineRenderer.positionCount = 0;
    }

    private void DrawRay(LineRenderer lineRenderer, Vector3 p1, Vector3 p2)
    {
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = Color.blue;
        lineRenderer.endColor = Color.blue;
        lineRenderer.SetPosition(0, p1);
        lineRenderer.SetPosition(1, p2);
    }

    public void ShowCameraFOV()
    {
        if (showFOV)
        {
            var up = player.transform.up;
            var forward = player.transform.forward;

            var forwardLeft = Quaternion.AngleAxis(-FOV1, up) * forward;
            var forwardRight = Quaternion.AngleAxis(FOV2, up) * forward;

            var p1 = player.transform.position;
            // left
            DrawRay(fovLine1, p1, p1 + forwardLeft * FOVLength);
            // right
            DrawRay(fovLine2, p1, p1 + forwardRight * FOVLength);
        }
        else
        {
            UnDrawRay(fovLine1);
            UnDrawRay(fovLine2);
        }
    }

    public void DestoryObjectImmediate(GameObject go)
    {
        if (go != null)
        {
            GameObject.DestroyImmediate(go);
        }
    }

    public void RemoveAllChildren(GameObject go)
    {
        int count = go.transform.childCount;
        for (int i = count - 1; i >= 0; i--)
        {
            DestoryObjectImmediate(go.transform.GetChild(i).gameObject);
        }
    }

    public void CloneObject(GameObject go, Transform parent)
    {
        var cloned = Instantiate(go, parent);
        // update position and rotation since the parent object is changed
        cloned.transform.position = go.transform.position;
        cloned.transform.rotation = go.transform.rotation;
        // rename
        cloned.name = go.name;
        // make invisible
        cloned.SetActive(false);
    }

    public GameObject CloneObject(GameObject go, GameObject parent, bool copyLabel = false)
    {
        var cloned = Instantiate(go, parent.transform);
        // update position and rotation since the parent object is changed
        cloned.transform.position = go.transform.position;
        cloned.transform.rotation = go.transform.rotation;
        if (copyLabel)
        {
            cloned.name = go.name;
        }

        return cloned;
    }

    #region Integer Programming

    /// <summary>
    /// Filter objects by sites tags
    /// For convenience, add children from the constructed voronoi graph root
    /// </summary>
    /// <param name="tags"></param>
    /// <returns></returns>
    private List<GameObject> FilterObjectsBySiteTags(List<String> tags)
    {
        List<GameObject> result = new List<GameObject>();

        var count = tags.Count;
        for (int i = 0; i < count; i++)
        {
            var site = siteNameGameObjectDic[tags[i]];
            var ccount = site.transform.childCount;
            for (int j = 0; j < ccount; j++)
            {
                var obj = site.transform.GetChild(j).gameObject;
                result.Add(obj);
            }
        }

        return result;
    }


    /// <summary>
    /// Get the cost between the detected object and the possible object
    /// cost = dimension_cost * (distance_cost * d_weight + rotation_cost * r_weight)
    /// </summary>
    /// <param name="detected"></param>
    /// <param name="possible"></param>
    /// <param name="useDistance"></param>
    /// <param name="useRotation"></param>
    /// <param name="useDimension"></param>
    /// <param name="dimensionCost"></param>
    /// <returns></returns>
    private float GetDetectedPossibleCost(GameObject detected, GameObject possible, bool useDistance, bool useRotation,
        bool useDimension, float dimensionCost)
    {
        var dcost = (detected.transform.position - possible.transform.position).magnitude;
        var rcost = Math.Abs(detected.transform.rotation.eulerAngles.y - possible.transform.rotation.eulerAngles.y);

        if (normalizeCost)
        {
            dcost = NormalizeDistanceCost(dcost);
            rcost = NormalizeRotationCost(rcost);
        }

        float result = 0.0f;

        // Debug.Log($"dcost: {dcost.ToString("f5")} rcost: {rcost.ToString("f5")}");
        if (useDistance) result += dcost * distanceWeight;
        if (useRotation) result += rcost * rotationWeight;
        if (useDimension) result *= dimensionCost;
        return result;
    }

    /// <summary>
    /// Normalize distance cost
    /// </summary>
    /// <param name="cost"></param>
    /// <returns></returns>
    private float NormalizeDistanceCost(float cost)
    {
        var size = mapGeneratorPreview.meshSize * 1.4142135623730951f; //sqrt(2)
        return cost / size;
    }

    /// <summary>
    /// Normalize rotation cost
    /// </summary>
    /// <param name="cost"></param>
    /// <returns></returns>
    private float NormalizeRotationCost(float cost)
    {
        // use sin to normalize to (0,1)
        // sin(cost/2.0)
        return (float)Math.Sin(cost * (Math.PI) / 360);
    }

    /// <summary>
    /// Get all detected objects (added manually)
    /// </summary>
    /// <returns></returns>
    private List<GameObject> GetAllDetectedObjects()
    {
        return detectedObjects;
    }

    private void UpdatePossiblesString(List<GameObject> objs)
    {
        possibles = "";
        totalCost = "";
        if (detectedObjects.Count > 0)
        {
            List<string> ss = new List<string>();
            foreach (var obj in objs)
            {
                ss.Add(GetLabel(obj));
            }

            possibles = String.Join(", ", ss);
        }
    }

    /// <summary>
    /// Filter sites by the probability normal distribution sigma
    /// </summary>
    /// <returns></returns>
    private List<string> FilterSitesByProbSigma()
    {
        List<string> result = new List<string>();
        var total = 0.0f;
        foreach (var kv in sortedSitesWithProb)
        {
            var site = kv.Key;

            var prob = kv.Value;
            // sorted
            if (total < sitesProbThreshold)
            {
                result.Add(site);
                total += prob;
            }
            else
            {
                break;
            }
        }

        withinSigma = String.Join(", ", result);

        return result;
    }

    /// <summary>
    /// Get all objects within the visible sites
    /// </summary>
    /// <returns></returns>
    public List<GameObject> GetObjectsOfWithinFOVSites()
    {
        return FilterObjectsBySiteTags(FilterSitesByFOV());
    }

    /// <summary>
    /// Assign objects
    /// </summary>
    /// <param name="detected"></param>
    public void AssignObjects(List<GameObject> detected)
    {
        var possibleObjs = FilterObjectsBySiteTags(FilterSitesByProbSigma());
        // if (sitesProbThreshold == 1.0f)
        // {
        //     possibleObjs = GetObjectsOfWithinFOVSites();
        // }

        if (detected.Count > possibleObjs.Count)
        {
            Debug.LogError("Detected objects count > Possible objects count");
            return;
        }

        // cache dimension cost
        CacheDimensionCosts(detected);

        // update string
        UpdatePossiblesString(possibleObjs);

        var rows = detected.Count;
        int fovCount = possibleObjs.Count;
        double[] cost = new double[rows * fovCount];
        // fill cost
        var cursor = 0;
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < fovCount; j++)
            {
                var dimensionCost = QueryDimensionCost(i, detected[i], possibleObjs[j]);
                cost[cursor++] = GetDetectedPossibleCost(detected[i], possibleObjs[j], true, true, true, dimensionCost);
            }
        }

        // run LP solver
        var result = LPAssignmentSolver(rows, fovCount, cost);

        if (result.Count > 0)
        {
            // update visualization
            foreach (var key in result.Keys)
            {
                // key is detected detected object,
                // value is possible chair
                // assign id from the value
                var value = result[key];
                var name = possibleObjs[value].name;
                // update name for detected chair and show it
                detected[key].name = name;
                AddTextMesh(detected[key], true, estimatedLabelColor);
            }
        }
    }

    private void AssignObjects()
    {
        AssignObjects(GetAllDetectedObjects());
    }

    private Dictionary<int, int> LPAssignmentSolver(int rows, int fovCount, double[] cost)
    {
        Dictionary<int, int> result = new Dictionary<int, int>();

        if (rows < 1 || fovCount < 1)
        {
            // Debug.LogWarning("Illegal input for LPAssignmentSolver");
            return result;
        }

        int cols = rows * fovCount;
        using (LpSolve lp = LpSolve.make_lp(rows, cols))
        {
            if (lp == null)
            {
                Debug.LogError("couldn't construct a new model...");
                return result;
            }

            // set col names
            for (int i = 0; i < cols; i++)
            {
                lp.set_col_name(i + 1, $"C{i + 1}");
            }

            // // set row names
            // for (int i = 0; i < rows + fovCount; i++)
            // {
            //     lp.set_row_name(i + 1, $"R{i + 1}");
            // }

            // set binary
            for (int i = 0; i < cols; i++)
            {
                lp.set_binary(i + 1, true);
            }

            // set row add mode
            lp.set_add_rowmode(true);

            // for each row, the detected chair must be from the FOV chairs
            double[] row = new double[fovCount];
            for (int i = 0; i < fovCount; i++)
            {
                row[i] = 1;
            }

            // col numbers
            int batch = 0;
            int[] colIndices = new int[fovCount];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < fovCount; j++)
                {
                    colIndices[j] = i * fovCount + j + 1;
                }

                // add constraint
                // here = 1 means that every detected chair should be assigned.
                if (lp.add_constraintex(fovCount, row, colIndices, lpsolve_constr_types.EQ, 1) == false)
                {
                    Debug.LogError("Failed to add constraint 1");
                    return result;
                }
            }

            // add more contraint
            for (int i = 0; i < rows; i++)
            {
                row[i] = -1;
            }

            for (int i = 0; i < fovCount; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    colIndices[j] = j * fovCount + i + 1;
                }

                // add constraint
                // here =-1 means every sink(chair within FOV) should be consumed
                // if the detected number < chairs within FOV, the should be >= -1, it means some sinks can be unassigned.
                var type = lpsolve_constr_types.EQ;
                if (rows < fovCount)
                {
                    type = lpsolve_constr_types.GE;
                }

                if (lp.add_constraintex(rows, row, colIndices, type, -1) == false)
                {
                    Debug.LogError("Failed to add constraint 2");
                    return result;
                }
            }

            lp.set_add_rowmode(false);
            // set objective function
            int[] colno = new int[cols];
            for (int i = 0; i < cols; i++)
            {
                colno[i] = i + 1;
            }

            if (lp.set_obj_fnex(cols, cost, colno) == false)
            {
                Debug.LogError("Failed to set objective function.");
                return result;
            }

            // set the object direction to maximize
            lp.set_minim();

            // just out of curioucity, now show the model in lp format on screen
            // this only works if this is a console application. If not, use write_lp and a filename
            lp.write_lp($"{saveModelName}.lp");

            // I only want to see important messages on screen while solving
            lp.set_verbose(lpsolve_verbosity.IMPORTANT);

            // Now let lpsolve calculate a solution
            lpsolve_return s = lp.solve();
            if (s != lpsolve_return.OPTIMAL)
            {
                Debug.LogError("failed to get an optional result.");
                return result;
            }

            // objective value
            // Debug.LogWarning("Objective value: " + lp.get_objective());

            totalCost = lp.get_objective().ToString("f5");

            // a solution is calculated, now lets get some results
            // variable values
            double[] vars = new double[cols];
            lp.get_variables(vars);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < fovCount; j++)
                {
                    if (vars[i * fovCount + j] == 1.0)
                    {
                        // Debug.LogWarning($"Match found: (R{i + 1}, C{j + 1}).");
                        result.Add(i, j);
                    }
                }
            }

            return result;
        }
    }

    #endregion

    #region Detected Objects

    private void InitializeDetectedObjects()
    {
        detectedObjects = new List<GameObject>();
        detectedObjectCursor = -1;
        UpdateObjectMaterial();
    }

    private void AddDetectedObject(GameObject go)
    {
        detectedObjects.Add(go);
        // update cursor
        detectedObjectCursor = detectedObjects.Count - 1;
        // controlling the chair
        controllingMode = ControllingMode.Object;
        UpdateObjectMaterial();
    }

    private void DeleteDetectedObject(GameObject obj)
    {
        if (obj == null) return;
        if (detectedObjects.Remove(obj))
        {
            // destroy
            DestoryObjectImmediate(obj);
            detectedObjectCursor -= 1;
            if (detectedObjectCursor < 0)
            {
                if (detectedObjects.Count > 0)
                {
                    detectedObjectCursor = 0;
                }
                else
                {
                    detectedObjectCursor = -1;
                }
            }
        }

        controllingMode = ControllingMode.Object;
        UpdateObjectMaterial();
    }

    private void MoveToPrevious()
    {
        if (detectedObjects.Count > 0)
        {
            detectedObjectCursor -= 1;
            if (detectedObjectCursor < 0)
            {
                detectedObjectCursor = detectedObjects.Count - 1;
            }
        }
        else
        {
            detectedObjectCursor = -1;
        }

        controllingMode = ControllingMode.Object;
        UpdateObjectMaterial();
    }

    private void MoveToNext()
    {
        if (detectedObjects.Count > 0)
        {
            detectedObjectCursor += 1;
            detectedObjectCursor %= detectedObjects.Count;
        }
        else
        {
            detectedObjectCursor = -1;
        }

        controllingMode = ControllingMode.Object;
        UpdateObjectMaterial();
    }

    private GameObject GetCurrentObject()
    {
        if (detectedObjectCursor > -1)
        {
            return detectedObjects[detectedObjectCursor];
        }

        return null;
    }

    private GameObject GetControllingObject()
    {
        if (controllingPlayer()) return player;
        if (controllingMode == ControllingMode.Object) return GetCurrentObject();
        return null;
    }

    private void UpdateObjectMaterial()
    {
        // all to default first
        foreach (var obj in detectedObjects)
        {
            MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();

            var material = GetDefaultMaterial(obj);
            meshRenderer.material = material;
        }

        if (controllingMode == ControllingMode.Object && detectedObjectCursor > -1)
        {
            var obj = detectedObjects[detectedObjectCursor];
            MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
            meshRenderer.material = selectedMaterial;
        }
    }

    #endregion

    #region Update Event

    // Update is called once per frame
    void Update()
    {
        if (keyboardInput)
        {
            // get controlling object first
            var obj = GetControllingObject();
            if (obj == null)
            {
                var weight = distanceWeight;
                if (controllingMode == ControllingMode.Rotation)
                {
                    weight = rotationWeight;
                }

                // control weight
                if (Input.GetKey(KeyCode.UpArrow))
                {
                    weight *= increaseRatio;
                }

                if (Input.GetKey(KeyCode.DownArrow))
                {
                    weight *= decreaseRatio;
                }

                // override weight
                if (controllingMode == ControllingMode.Rotation)
                {
                    rotationWeight = weight;
                }
                else if (controllingMode == ControllingMode.Distance)
                {
                    distanceWeight = weight;
                }

                // re-assign
                AssignObjects();
                return;
            }

            var deltaX = 0.0f;
            var deltaZ = 0.0f;

            var rotationY = 0.0f;

            if (Input.GetKey(KeyCode.UpArrow))
            {
                deltaZ = 0.1f;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                deltaX = 0.1f;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                deltaZ = -0.1f;
            }

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                deltaX = -0.1f;
            }

            obj.transform.Translate(deltaX, 0, deltaZ);
            // rotation

            if (Input.GetKey(KeyCode.A))
            {
                // left: y--
                rotationY = -1.0f;
            }

            if (Input.GetKey(KeyCode.D))
            {
                // right: y++
                rotationY = 1.0f;
            }

            obj.transform.Rotate(0.0f, rotationY, 0.0f);
        }

        // update attributes
        if (updateOnPlay)
        {
            ShowOrientation();
            ShowCameraFOV();
            Run();
        }
        else
        {
            ResetLineRenders();
        }
    }

    #endregion

    private void OnApplicationQuit()
    {
        RemoveAllChildren(colliderContainer);
    }

    #region Button Press Event

    public void AddNewTable()
    {
        var template = GetTemplate("table");
        // clone table
        var obj = CloneObject(template, detectedRoot);
        // set active
        obj.SetActive(true);
        AddDetectedObject(obj);
    }

    public void AddNewChair()
    {
        var template = GetTemplate("chair");
        // clone chair
        var obj = CloneObject(template, detectedRoot);
        // set active
        obj.SetActive(true);
        AddDetectedObject(obj);
    }

    public void DelObject()
    {
        var current = GetCurrentObject();
        DeleteDetectedObject(current);
    }

    public void NextObject()
    {
        MoveToNext();
    }

    public void PrevObject()
    {
        MoveToPrevious();
    }

    public void MoveObject()
    {
        controllingMode = ControllingMode.Object;
        UpdateObjectMaterial();
    }

    public void MovePlayer()
    {
        controllingMode = ControllingMode.Player;
        UpdateObjectMaterial();
    }

    public void UpdateDistanceWeight()
    {
        controllingMode = ControllingMode.Distance;
        UpdateObjectMaterial();
    }

    public void UpdateRotationWeight()
    {
        controllingMode = ControllingMode.Rotation;
        UpdateObjectMaterial();
    }

    #endregion

    #region GUI Draw Event

    private void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 100, 30), "New Table"))
        {
            AddNewTable();
        }

        if (GUI.Button(new Rect(10, 40, 100, 30), "New Chair"))
        {
            AddNewChair();
        }

        if (GUI.Button(new Rect(110, 10, 40, 30), "Del"))
        {
            DelObject();
        }

        if (GUI.Button(new Rect(170, 10, 80, 30), "Next"))
        {
            NextObject();
        }

        if (GUI.Button(new Rect(250, 10, 80, 30), "Prev"))
        {
            PrevObject();
        }

        // if (GUI.Button(new Rect(280, 10, 80, 30), "Prev Chair")) {
        // }

        if (GUI.Button(new Rect(370, 10, 100, 30), "Move Object"))
        {
            MoveObject();
        }

        if (GUI.Button(new Rect(470, 10, 100, 30), "Move Player"))
        {
            MovePlayer();
        }

        GUI.TextArea(new Rect(600, 10, 150, 50), $"Controlling Mode:\n{controllingMode}");

        GUI.TextArea(new Rect(800, 10, 180, 30), $"Detected Objects Count: {detectedObjects.Count}");

        if (GUI.Button(new Rect(1000, 10, 80, 50), "Distance\nWeight: "))
        {
            UpdateDistanceWeight();
        }

        GUI.TextField(new Rect(1080, 10, 50, 30), $"{distanceWeight}");

        if (GUI.Button(new Rect(1150, 10, 80, 50), "Rotation\nWeight: "))
        {
            UpdateRotationWeight();
        }

        GUI.TextField(new Rect(1230, 10, 50, 30), $"{rotationWeight}");

        if (GUI.Button(new Rect(1300, 10, 30, 30), $"1σ"))
        {
            sitesProbThreshold = sigma1;
        }

        if (GUI.Button(new Rect(1330, 10, 30, 30), $"2σ"))
        {
            sitesProbThreshold = sigma2;
        }

        if (GUI.Button(new Rect(1360, 10, 30, 30), $"3σ"))
        {
            sitesProbThreshold = sigma3;
        }

        if (GUI.Button(new Rect(1390, 10, 30, 30), $"All"))
        {
            sitesProbThreshold = 1.0f;
        }

        GUI.TextField(new Rect(1300, 40, 120, 30), $"{sitesProbThreshold}");

        if (GUI.Button(new Rect(1300, 70, 120, 30), "Softmax"))
        {
            useSoftmax = true;
        }

        if (GUI.Button(new Rect(1300, 100, 120, 30), "Non-Softmax"))
        {
            useSoftmax = false;
        }

        String text = $"Prob of Sites:\n{siteProbText}\n" +
                      $"\nWithin Sigma: {withinSigma}\n" +
                      // $"\nWithin FOV: {withinFOV}\n" +
                      $"\nPossibles: {possibles}\n" +
                      $"\nTotal cost: {totalCost}\n";
        // $"\nProb of Chairs Under Site:\n{siteChairsProbText}\n" +
        // $"\nProb of Chairs:\n{chairsProbText}\n" +
        // $"\n{determinedOrder}\n";
        // if (useShortTerm)
        // {
        //     // replace table_ with empty and chair_ with empty
        //     text = text.Replace("table_", "").Replace("chair_", "");
        // }

        DrawText(text);
    }

    #endregion

    #region Visibility Checking

    // Is one site within the FOV
    private bool IsPointWithinFOV(Vector3 pos, Vector3 fov1, Vector3 fov2, Vector3 site)
    {
        // only when angle(pos->site, pos->fov1) < angle(pos->fov1, pos->fov2)
        // and angle(pos->site, pos->fov2) < angle(pos->fov1, pos->fov2), return true
        // otherwise, return false

        var p = new Vector2(pos.x, pos.z);
        var f1 = new Vector2(fov1.x, fov1.z);
        var f2 = new Vector2(fov2.x, fov2.z);
        var s = new Vector2(site.x, site.z);

        float angle = Vector2.Angle(f1 - p, f2 - p);
        float angle1 = Vector2.Angle(s - p, f1 - p);
        float angle2 = Vector2.Angle(s - p, f2 - p);

        return (angle1 <= angle && angle2 <= angle);
    }

    private bool IsSitePartiallyWithinFOV(Vector3 pos, Vector3 fov1, Vector3 fov2, Vector3 site)
    {
        var mg = mapGeneratorPreview.GetMapGraph();
        var boundriesForSite = mg.GetBoundriesForSite(mg.GetVoronoi(), new Vector2(site.x, site.z));
        foreach (var boundary in boundriesForSite)
        {
            var p0 = boundary.p0.Value;
            var p1 = boundary.p1.Value;
            var f1 = IsPointWithinFOV(pos, fov1, fov2, new Vector3(p0.x, 0, p0.y));
            var f2 = IsPointWithinFOV(pos, fov1, fov2, new Vector3(p1.x, 0, p1.y));
            if (f1 || f2) return true;
        }

        return false;
    }

    /// <summary>
    /// Is point within FOV
    /// </summary>
    /// <param name="player"></param>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool IsPointWithinFOV(GameObject player, Vector3 point)
    {
        var pos = player.transform.position;
        var up = player.transform.up;
        var forward = player.transform.forward;

        var fov1 = pos + Quaternion.AngleAxis(-FOV1, up) * forward * FOVLength;
        var fov2 = pos + Quaternion.AngleAxis(FOV2, up) * forward * FOVLength;

        return IsPointWithinFOV(pos, fov1, fov2, point);
    }

    /// <summary>
    /// Is point within FOV
    /// Use this player's position
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool IsPointWithinFOV(Vector3 point)
    {
        return IsPointWithinFOV(player, point);
    }
    
    /// <summary>
    /// Is point within FOV
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool IsPointWithinFOV(Transform transform, Vector3 point)
    {
        var pos = transform.position;
        var up = transform.up;
        var forward = transform.forward;
        var fov1 = pos + Quaternion.AngleAxis(-FOV1, up) * forward * FOVLength;
        var fov2 = pos + Quaternion.AngleAxis(FOV2, up) * forward * FOVLength;

        return IsPointWithinFOV(pos, fov1, fov2, point);
    }

    /// <summary>
    /// Filter out objects which are within the player's FOV
    /// </summary>
    /// <param name="objects"></param>
    /// <param name="transfrom"></param>
    /// <returns></returns>
    public List<GameObject> FilterObjectsWithinFOV(List<GameObject> objects, Transform transfrom)
    {
        List<GameObject> result = new List<GameObject>();
        foreach (var obj in objects)
        {
            if (IsPointWithinFOV(transfrom, obj.transform.position))
            {
                if (rangeSimulation)
                {
                    // use range to filter objects
                    var distance = DistanceOf2D(transfrom.position, obj.transform.position);
                    if(distance > rangeMaximum)
                    {
                        continue;
                    }
                }
                result.Add(obj);
            }
        }

        return result;
    }

    #endregion


    private float GetDistance(Vector3 pos, Vector2 site)
    {
        var p = new Vector2(pos.x, pos.z);
        var direction = site - p;
        var distanceSqr = direction.sqrMagnitude;
        return distanceSqr;
    }

    // Sort sites by distance between player and site
    private List<string> SortSitesByDistance()
    {
        List<String> result = new List<string>();
        Dictionary<Vector2, String> dictionary = mapGeneratorPreview.SiteTagByLocation();

        var position = player.transform.position;

        var list = dictionary.ToList();

        list.Sort((pair1, pair2) =>
        {
            var d1 = GetDistance(position, pair1.Key);
            var d2 = GetDistance(position, pair2.Key);
            return d1.CompareTo(d2);
        });

        foreach (var obj in list)
        {
            // Debug.Log($"{obj.Key} - {obj.Value}");
            result.Add(obj.Value);
        }

        return result;
    }

    private List<String> SortSitesByOrientation()
    {
        List<String> result = new List<string>();
        Dictionary<Vector2, String> dictionary = mapGeneratorPreview.SiteTagByLocation();
        var forward = player.transform.forward.normalized;
        var pos = player.transform.position;

        var list = dictionary.ToList();

        list.Sort((pair1, pair2) =>
        {
            var d1 = (new Vector3(pair1.Key.x, 0, pair1.Key.y) - pos).normalized;
            var d2 = (new Vector3(pair2.Key.x, 0, pair2.Key.y) - pos).normalized;

            return (d1 - forward).sqrMagnitude.CompareTo((d2 - forward).sqrMagnitude);
        });

        foreach (var obj in list)
        {
            // Debug.Log($"{obj.Key} - {obj.Value}");
            result.Add(obj.Value);
        }

        return result;
    }

    /// <summary>
    /// Filter sites by FOV (within or outside FOV)
    /// </summary>
    /// <returns></returns>
    public List<string> FilterSitesByFOV()
    {
        List<String> result = new List<string>();
        var pos = player.transform.position;
        var up = player.transform.up;
        var forward = player.transform.forward;

        var fov1 = pos + Quaternion.AngleAxis(-FOV1, up) * forward * FOVLength;
        var fov2 = pos + Quaternion.AngleAxis(FOV2, up) * forward * FOVLength;

        Dictionary<Vector2, String> dictionary = mapGeneratorPreview.SiteTagByLocation();

        // closest node, it's self
        var closestNode = mapGeneratorPreview.GetMapGraph().GetClosestNode(pos.x, pos.z);
        // add self
        result.Add(closestNode.tag);

        foreach (var pair in dictionary)
        {
            var site = pair.Key;
            var tag = pair.Value;

            if (tag == closestNode.tag) continue;

            if (IsSitePartiallyWithinFOV(pos, fov1, fov2, new Vector3(site.x, 0, site.y)))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    private List<KeyValuePair<string, float>> SortDictionary(Dictionary<string, float> dic, bool by_key, bool reverse)
    {
        var list = dic.ToList();

        list.Sort((pair1, pair2) =>
        {
            if (reverse)
            {
                var t = pair2;
                pair2 = pair1;
                pair1 = t;
            }

            if (by_key) return pair1.Key.CompareTo(pair2.Key);

            return pair1.Value.CompareTo(pair2.Value);
        });

        return list;
    }

    private string KeyValuePairToString(List<KeyValuePair<string, float>> input, string sep = " - ",
        int auto_break = -1)
    {
        List<String> result = new List<string>();
        var count = 1;
        foreach (var pair in input)
        {
            var s = $"[{pair.Key}]: {pair.Value.ToString("f4")}";
            if (auto_break != -1 && count == auto_break)
            {
                s += "\n";
                count = 0;
            }

            result.Add(s);
            count += 1;
        }

        return String.Join(sep, result);
    }

    private Dictionary<string, float> GetAllSitesProbs(Vector3 pos)
    {
        var dic = GetAllSitesDistances(pos);
        var list = dic.ToList();

        List<string> keys = dic.Keys.ToList();

        // calculate probabilities
        // softmax
        if (useSoftmax)
        {
            var sum = dic.ToList().Sum(x => Convert.ToSingle(Math.Exp(1.0f / x.Value)));
            foreach (var key in keys)
            {
                var prob = Math.Exp(1.0f / dic[key]) / sum;
                dic[key] = (float)prob;
            }
        }
        else
        {
            var sum = list.Sum(x => Convert.ToSingle(1.0f / x.Value));

            foreach (var key in keys)
            {
                var prob = 1.0f / dic[key] / sum;
                dic[key] = prob;
            }
        }

        return dic;
    }

    public void Run()
    {
        var pos = player.transform.position;

        // sites' probs
        Dictionary<string, float> sitesProbs = GetAllSitesProbs(pos);

        // to sorted list
        sortedSitesWithProb = SortDictionary(sitesProbs, false, true);
        siteProbText = KeyValuePairToString(sortedSitesWithProb);
        // sites within FOV
        withinFOV = String.Join(", ", FilterSitesByFOV());
        // assign objects
        AssignObjects();
    }

    private Dictionary<string, float> GetAllChairsProbs(Dictionary<string, float> sitesProbs,
        Dictionary<string, Dictionary<string, float>> chairsProbs)
    {
        Dictionary<string, float> result = new Dictionary<string, float>();
        foreach (var kvp in chairsProbs)
        {
            var site = kvp.Key;
            var probs = kvp.Value;
            var siteProb = sitesProbs[site];

            foreach (var kv in probs)
            {
                var prob = siteProb * kv.Value;
                result.Add(kv.Key, prob);
            }
        }

        return result;
    }

    private List<string> DetermineOrderEmptyFOV(List<string> distance, List<string> orientation)
    {
        var length = distance.Count;
        Dictionary<string, int> tagWeights = new Dictionary<string, int>();
        for (int i = 0; i < length; i++)
        {
            tagWeights.Add(distance[i], i);
        }

        for (int i = 0; i < length; i++)
        {
            tagWeights[orientation[i]] += i;
        }

        var list = tagWeights.ToList();
        list.Sort((pair1, pair2) =>
        {
            // same value
            if (pair1.Value.CompareTo(pair2.Value) == 0)
            {
                var idx1 = distance.IndexOf(pair1.Key);
                var idx2 = distance.IndexOf(pair2.Key);
                return idx1.CompareTo(idx2);
            }

            return pair1.Value.CompareTo(pair2.Value);
        });

        List<String> result = new List<string>();
        foreach (var pair in list)
        {
            result.Add(pair.Key);
        }

        return result;
    }

    private List<string> DetermineOrder(List<string> distance, List<string> orientation, List<string> fov)
    {
        // 1. FOV is empty, no site is within FOV.
        //      return distance // distance + orientation, distance is more important, return weighted sum in ascend order
        // 2. FOV is full, all sites are within FOV.
        //      Distance is more important, return distance
        // 3. FOV is not empty and not full, some sites are within FOV.
        //     Move sites within FOV ahead, and append the remaining sites
        var length = distance.Count;
        if (fov.Count == 0) return distance;
        if (fov.Count == length) return distance;

        List<String> result = new List<string>();

        // sites within FOV
        for (int i = 0; i < length; i++)
        {
            if (fov.Contains(distance[i]))
            {
                result.Add(distance[i]);
            }
        }

        for (int i = 0; i < length; i++)
        {
            if (!fov.Contains(distance[i]))
            {
                result.Add(distance[i]);
            }
        }


        return result;
    }

    private void DrawText(string text)
    {
        GUI.Label(new Rect(10, 100, 200, 200), text, guiStyleSmall);
    }

    private double Power(float v)
    {
        return v * v;
    }

    // refer: https://stackoverflow.com/questions/10983872/distance-from-a-point-to-a-polygon
    // NOTE: HandleUtility.DistancePointToLine(Vector2 x, Vector2 p1, Vector2 p2) doesn't work when the projected point is not within lineSegment(p1,p2)
    private float PointToLineDistance(Vector2 x, Vector2 p1, Vector2 p2)
    {
        float dist = 0.0f;
        var r = Vector2.Dot(p2 - p1, x - p1);
        var mag = (p2 - p1).magnitude;
        r /= (mag * mag);
        if (r < 0)
        {
            dist = (x - p1).magnitude;
        }
        else if (r > 1)
        {
            dist = (p2 - x).magnitude;
        }
        else
        {
            dist = (float)Math.Sqrt(Power((x - p1).magnitude) - Power(r * ((p2 - p1).magnitude)));
        }

        return dist;
    }

    private float DistanceOf2D(Vector3 p1, Vector3 p2)
    {
        return (new Vector2(p1.x, p1.z) - new Vector2(p2.x, p2.z)).magnitude;
    }

    private float DistanceOf2D(Vector2 p1, Vector2 p2)
    {
        return (p2 - p1).magnitude;
    }

    private Dictionary<string, Dictionary<string, float>> GetAllChairsProbsUnderSite(Vector3 pos,
        string chair_prefix)
    {
        // all sites' names
        var mg = mapGeneratorPreview.GetMapGraph();
        var sitesAll = mg.GetVoronoi().GetSites();

        // {site(table): {chair:prob}}
        Dictionary<string, Dictionary<string, float>> result = new Dictionary<string, Dictionary<string, float>>();

        foreach (var site in sitesAll)
        {
            var name = mapGeneratorPreview.SiteTagByLocation()[site.Coord];
            var siteRoot = siteNameGameObjectDic[name];
            var siteChairsProb = GetChairProbabilityOfSite(pos, siteRoot, chair_prefix);
            result.Add(name, siteChairsProb);
        }

        return result;
    }

    // Get GameObject for site
    private GameObject GetSiteRoot(GameObject root, string site_name, string table_prefix)
    {
        var count = root.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.transform.GetChild(i);
            if (child.name.StartsWith(table_prefix) && child.name == site_name)
                return child.gameObject;
        }

        return null;
    }


    // Calculate probabilities of chairs for one site
    private Dictionary<string, float> GetChairProbabilityOfSite(Vector3 pos, GameObject site_root, string chair_prefix)
    {
        Dictionary<string, float> chairProb = new Dictionary<string, float>();
        int count = site_root.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = site_root.transform.GetChild(i);
            if (child.name.StartsWith(chair_prefix))
            {
                // get distance
                var distance = DistanceOf2D(pos, child.position);
                chairProb.Add(child.name, distance);
            }
        }

        // calculate probability
        var sum = chairProb.ToList().Sum(x => Convert.ToSingle(1.0 / x.Value));

        List<string> keys = chairProb.Keys.ToList();
        foreach (var key in keys)
        {
            var prob = 1.0f / chairProb[key] / sum;
            chairProb[key] = prob;
        }

        return chairProb;
    }

    // Get Distance to All Sites
    private Dictionary<string, float> GetAllSitesDistances(Vector3 pos)
    {
        // Map for (tag, distance)
        Dictionary<string, float> sitesDistanceMap = new Dictionary<string, float>();

        // min distance to site boundary
        float minDistanceToBoundary = float.MaxValue;
        // min distance to site center excluding self
        Vector2 closestSiteCenter = Vector2.zero;

        // closest site
        var mg = mapGeneratorPreview.GetMapGraph();
        var closestNode = mg.GetClosestNode(pos.x, pos.z);

        var cp = closestNode.centerPoint;
        var p = new Vector2(cp.x, cp.z);

        // all lines around this site
        List<LineSegment> lines = mg.GetBoundriesForSite(mg.GetVoronoi(), p);
        var pos2d = new Vector2(pos.x, pos.z);
        
        // only consider the neighbor sites
        // there is a bug in NeighborSitesForSite
        // var sites = mg.GetVoronoi().NeighborSitesForSite(p);
        // add sites that are not the direct neighbor of the nearest site
        var sitesAll = mg.GetVoronoi().GetSites();
        
        // loop boundaries to cal the distance to another sites
        foreach (var line in lines)
        {
            // distance between input point and the other site
            var distance = PointToLineDistance(pos2d, line.p0.Value, line.p1.Value);
        
            // loop all other site to get the distances
            foreach (var site in sitesAll)
            {
                if (mg.SiteOwnsBoundary(mg.GetVoronoi(), site.Coord, line))
                {
                    var tag = mapGeneratorPreview.SiteTagByLocation()[site.Coord];
                    // add to map
                    sitesDistanceMap.Add(tag, distance);
                    if (distance < minDistanceToBoundary)
                    {
                        minDistanceToBoundary = distance;
                        closestSiteCenter = site.Coord;
                    }
        
                    break;
                }
            }
        }
        
        foreach (var site in sitesAll)
        {
            var tag = mapGeneratorPreview.SiteTagByLocation()[site.Coord];
            if (tag != closestNode.tag && !sitesDistanceMap.ContainsKey(tag)) // not self and not in neighbor sites
            {
                var lslist = mg.GetBoundriesForSite(mg.GetVoronoi(), site.Coord);
                float distance = float.MaxValue;
                foreach (var ls in lslist)
                {
                    var d = PointToLineDistance(pos2d, ls.p0.Value, ls.p1.Value);
                    if (d < distance)
                    {
                        distance = d;
                    }
                }

                if (distance < minDistanceToBoundary)
                {
                    minDistanceToBoundary = distance;
                    closestSiteCenter = site.Coord;
                }

                // add to map
                sitesDistanceMap.Add(tag, distance);
            }
        }

        // use the ratio between object and site center to calculate an appropriate ratio
        // pos: input
        // cp: closest node center
        // closestSite Center: literal meaning
        // minDistanceToBoundary: min distance to boundary, paired with closestSiteCenter
        var adjustedDistance = DistanceOf2D(pos, cp) / DistanceOf2D(closestSiteCenter, new Vector2(pos.x, pos.z)) *
                               minDistanceToBoundary;
        sitesDistanceMap.Add(closestNode.tag, adjustedDistance);

        return sitesDistanceMap;
    }

    #region Dimension Cost Related

    /// <summary>
    /// Calculate single dimension cost, i.e., x - x, y - y, z - z,
    /// The result value is always greater or equal 1.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    private float CalSingleDimensionCost(float x, float y)
    {
        if (x > y) return x / y;
        return y / x;
    }

    /// <summary>
    /// Get the boundingbox size for one game object
    /// </summary>
    /// <param name="gameObject"></param>
    /// <returns></returns>
    private Vector3 GetBoundingBoxSize(GameObject gameObject)
    {
        MeshRenderer renderer;
        gameObject.TryGetComponent<MeshRenderer>(out renderer);
        if (renderer == null)
        {
            Debug.LogError($"{gameObject.name} doesn't have MeshRenderer.");
            return Vector3.zero;
        }

        return renderer.bounds.size;
    }

    /// <summary>
    /// Calculate the minimal dimension cost for x,y,z
    /// </summary>
    /// <param name="dSize">the size of detected</param>
    /// <param name="pSize">the size of possible</param>
    /// <returns></returns>
    private float CalMinDimensionCost(float[] dSize, float[] pSize)
    {
        double[] cost = new double[3 * 3]; //detected * possible
        // prepare for LP assignment
        var cursor = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                cost[cursor++] = CalSingleDimensionCost(dSize[i], pSize[j]);
            }
        }

        // run LP solve
        var result = LPAssignmentSolver(3, 3, cost);
        // this original objective function is to cal the sum, here we need to convert to the product
        // key is detected, value is possible
        double productCost = 1.0f;
        if (result.Count > 0)
        {
            // it should have very 3 pairs
            foreach (var key in result.Keys)
            {
                var value = result[key];
                // Debug.LogWarning($"Dimension Match: {key} - {value}");
                productCost *= cost[key * 3 + value];
            }
        }

        // Debug.LogWarning($"Dimension cost minimum: {productCost.ToString("f5")}");
        return (float)productCost;
    }

    /// <summary>
    /// Wrap for game object input
    /// </summary>
    /// <param name="detected"></param>
    /// <param name="possible"></param>
    /// <returns></returns>
    private float CalMinDimensionCost(GameObject detected, GameObject possible)
    {
        // dimension size
        var dbox = GetBoundingBoxSize(detected);
        float[] dSize = new float[] { dbox.x, dbox.y, dbox.z };

        var pbox = GetBoundingBoxSize(possible);
        float[] pSize = new float[] { pbox.x, pbox.y, pbox.z };

        return CalMinDimensionCost(dSize, pSize);
    }

    public void TestDimensionCost()
    {
        float[] detected = new float[] { 1, 2, 3 };
        float[] possible = new float[] { 1, 3, 2 };
        var result = CalMinDimensionCost(detected, possible); // 1
        Debug.LogWarning($"dimension cost: {result.ToString("f5")}");
    }

    #endregion

    #region Voronoi Graph Generation

    /// <summary>
    /// Construct sites' hierarchy with the voronoi graph result
    /// </summary>
    /// <param name="src">flatten obejects' root (original objects)</param>
    public void ConstructSiteHierarchy(GameObject src)
    {
        var mg = mapGeneratorPreview.GetMapGraph();

        int count = src.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var obj = src.transform.GetChild(i);
            if (obj.gameObject.activeSelf) // only active
            {
                // find the proper site
                var pos = obj.transform.position;
                var node = mg.GetClosestNode(pos.x, pos.z);
                // get node by tag
                var parent = siteNameGameObjectDic[node.tag];
                CloneObject(obj.gameObject, parent.transform);
            }
        }
    }

    /// <summary>
    /// Initializ siteNameGameObjectDic
    /// Clone first, and then set the dictionary to avoid changing the original sites objects
    /// </summary>
    private void SetSiteNameGameObjectDic()
    {
        if (siteNameGameObjectDic == null)
        {
            siteNameGameObjectDic = new Dictionary<string, GameObject>();
        }
        else
        {
            // clear first
            siteNameGameObjectDic.Clear();   
        }

        var root = mapGeneratorPreview.customizedSitesRoot;
        // remove all children
        RemoveAllChildren(graphRootGameObject);

        int count = root.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            GameObject child = root.transform.GetChild(i).gameObject;
            if (child.activeSelf) // only active
            {
                // clone
                var cloned = CloneObject(child, graphRootGameObject);
                // update name
                cloned.name = child.name;
                siteNameGameObjectDic.Add(cloned.name, cloned);
            }
        }
    }

    /// <summary>
    /// Cache dimension cost
    /// Since possible objects have duplicates, it is expensive to calculate dimension cost every time.
    /// </summary>
    public void CacheDimensionCosts(List<GameObject> detected)
    {
        if (dimensionCostCache == null)
        {
            dimensionCostCache = new Dictionary<int, Dictionary<string, float>>();
        }
        else
        {
            dimensionCostCache.Clear();
        }

        // loop detected objects
        var count = detected.Count;
        for (int i = 0; i < count; i++)
        {
            Dictionary<string, float> cache = new Dictionary<string, float>();
            var obj = detected[i];

            foreach (var template in templateDict.Values)
            {
                var cost = CalMinDimensionCost(obj, template);
                // add to cache
                cache.Add(template.name, cost);
            }

            // add to global cache
            dimensionCostCache.Add(i, cache);
        }
    }

    public float QueryDimensionCost(int dindex, GameObject detected, GameObject possible)
    {
        float cost = 1.0f;

        // there is only one template, which means the label is pre-determined.
        if (templateDict.Count == 1)
        {
            return cost;
        }
        
        if (dimensionCostCache.ContainsKey(dindex))
        {
            var dic = dimensionCostCache[dindex];

            //TODO: hard code to parse name pattern
            var key = GetCategory(possible);
            if (!dic.ContainsKey(key))
            {
                Debug.LogError($"Dimension cost doesn't have key {key}");
                Debug.LogError($"All keys are: {string.Join(",", dic.Keys.ToList())}");
            }
            else
            {
                cost = dic[key];
            }
        }
        else
        {
            Debug.LogWarning($"Can't find {dindex}:{detected.name} in dimensionCostCache.");
            cost = CalMinDimensionCost(detected, possible);
        }

        return cost;
    }

    /// <summary>
    /// Create Voronoi graph
    /// </summary>
    public void CreateVoronoiGraph()
    {
        // Generate Map first
        RemoveAllChildren(colliderContainer);
        ResetLineRenders();
        mapGeneratorPreview.GenerateMap();
        // update attributes
        mapGeneratorPreview.colliderManager.alwaysEnable = alwaysEnable;

        // update site name-gameobject pair
        SetSiteNameGameObjectDic();
        // construct root
        ConstructSiteHierarchy(originalObjectsGameObject);
    }

    #endregion

    #region Visualization For Debugging

    /// <summary>
    /// Show ground truth label
    /// </summary>
    public void ShowGroundTruthLabel()
    {
        var root = originalObjectsGameObject;
        var count = root.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.transform.GetChild(i);
            if (this.ignoreInvisible && !child.gameObject.activeSelf) continue;
            // add TextMesh object
            AddTextMesh(child.gameObject, this.showGroundTruthLabel, groundTruthLabelColor);
        }
    }

    /// <summary>
    /// Get category label of one object
    /// if xx_yy return xx, elif xx return xx
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    public string GetCategory(GameObject go)
    {
        var name = go.name;
        if (name.Contains("_"))
        {
            return name.Split('_')[0];
        }

        return name;
    }

    /// <summary>
    /// Get name of one object
    /// if xx_yy return yy, elif xx return xx
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    public string GetLabel(GameObject go)
    {
        var name = go.name;
        if (name.Contains("_"))
        {
            return name.Split('_')[1];
        }

        Debug.LogWarning($"{name} doesn't match xx_yy pattern.");
        return name;
    }

    /// <summary>
    /// Add TextMesh to show label
    /// </summary>
    /// <param name="go"></param>
    /// <param name="show">whether to show</param>
    /// <param name="color">color of the text</param>
    private void AddTextMesh(GameObject go, bool show, Color color)
    {
        var s = GetLabel(go);
        var tm = go.GetComponentInChildren<TextMesh>();
        if (tm != null)
        {
            if (show)
            {
                tm.text = s;
            }
            else
            {
                tm.text = "";
            }
        }
        else
        {
            GameObject child = new GameObject();

            TextMesh text = child.AddComponent(typeof(TextMesh)) as TextMesh;
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.LowerCenter;
            text.fontStyle = FontStyle.Bold;
            text.text = s;
            text.color = color;
            text.transform.localRotation = Quaternion.Euler(labelRotation);

            child.transform.parent = go.transform;
            child.transform.localPosition = labelPosition;
            child.transform.localScale = labelScale;
            // child.transform.localRotation = Quaternion.Euler(labelRotation);
        }
    }

    #endregion

    #region Getters

    /// <summary>
    /// Get all the original objects
    /// </summary>
    /// <returns></returns>
    public List<GameObject> GetAllObjects()
    {
        List<GameObject> result = new List<GameObject>();
        var root = originalObjectsGameObject;
        var count = root.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.transform.GetChild(i);
            if (this.ignoreInvisible && !child.gameObject.activeSelf) continue;
            result.Add(child.gameObject);
        }

        return result;
    }

    #endregion

    #region Setters
    
    /// <summary>
    /// Set threshold for external classes
    /// </summary>
    /// <param name="v"></param>
    public void SetThreshold(float v)
    {
        sitesProbThreshold = v;
    }

    #endregion

    #region Material Related

    /// <summary>
    /// Initialize templates
    /// </summary>
    /// <param name="gameobject"></param>
    /// <param name="material"></param>
    private void InitializeTemplates(bool gameobject, bool material)
    {
        if (!gameobject && !material) return;
        if (gameobject)
        {
            templateDict = new Dictionary<string, GameObject>();
        }

        if (material)
        {
            templateMaterialDict = new Dictionary<string, Material>();
        }

        var go = templatesRoot;
        var count = go.transform.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = go.transform.GetChild(i);
            var name = child.name;
            if (gameobject)
            {
                templateDict.Add(name, child.gameObject);
            }

            if (material)
            {
                MeshRenderer mr = child.GetComponent<MeshRenderer>();
                templateMaterialDict.Add(name, mr.material);
            }
        }
    }

    /// <summary>
    /// Get default material by name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private Material GetDefaultMaterial(string name)
    {
        if (templateMaterialDict != null)
            return templateMaterialDict[name];
        return null;
    }

    /// <summary>
    /// Get default material of one object
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    private Material GetDefaultMaterial(GameObject go)
    {
        var name = go.name;

        if (name.Contains('_')) // table_1
        {
            name = name.Split('_')[0];
        }
        else if (name.Contains('(')) // table(cloned)
        {
            name = name.Split('(')[0];
        }

        return GetDefaultMaterial(name);
    }

    #endregion

    #region Template Related

    /// <summary>
    /// Get template by name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private GameObject GetTemplate(string name)
    {
        if (templateDict == null) return null;
        return templateDict[name];
    }

    #endregion
}