using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DataCollector : MonoBehaviour
{
    #region Public Fields

    [Header("Reference")] public BatchEvaluation batchEvaluation;
    public RandomizedEvaluation randomizedEvaluation;

    [Header("Output")] 
    public Boolean layoutPicture = true;
    private string folder;

    #endregion

    #region Private Fields

    private StreamWriter writer = null;

    #endregion


    // Start is called before the first frame update
    void Start()
    {
        // EnrichingARInteraction/Eval/
        folder = Path.Combine(Application.dataPath, $"../Eval/");
        if (batchEvaluation != null)
        {
            batchEvaluation.newLine += OnNewLine;
        }

        if (randomizedEvaluation != null)
        {
            randomizedEvaluation.accuracyAvailable += OnAccuracyAvailable;
            randomizedEvaluation.layoutGenerated += OnLayoutGenerated;
        }

        // prepare writer
        EnsureWriter();
        // write header
        AppendString(ColHeader());
    }

    // Update is called once per frame
    void Update()
    {

    }

    #region Data Serialization

    /// <summary>
    /// Ensure writer is valid
    /// </summary>
    void EnsureWriter()
    {
        if (writer == null)
        {
            var filename = CSVFilename();
            var path = Path.Combine(folder, filename);
            writer = new StreamWriter(path);
            writer.AutoFlush = true;
        }
    }

    /// <summary>
    /// Write line via writer
    /// </summary>
    /// <param name="line"></param>
    /// <param name="newFile"></param>
    void WriteLine(string line, bool newFile = false)
    {
        if (newFile)
        {
            // close first
            if (writer != null)
            {
                CloseWriter();
            }
        }

        EnsureWriter();
        writer.WriteLine(line);
    }

    /// <summary>
    /// Append string to line end
    /// </summary>
    /// <param name="str"></param>
    void AppendString(string str)
    {
        EnsureWriter();
        writer.Write(str);
    }

    /// <summary>
    /// Close writer
    /// </summary>
    void CloseWriter()
    {
        if (writer != null)
        {
            writer.Close();
            writer = null;
        }
    }

    string ColHeader()
    {
        return "xm,xs,zm,zs,rm,rs,wd,wr";
    }

    /// <summary>
    /// Get the start string of a new line
    /// format: xm,xs,zm,zs,rm,rs,[accs]
    /// </summary>
    string RowHeader()
    {
        var re = randomizedEvaluation;
        var xm = re.XMean;
        var xs = re.XStdDev;
        var zm = re.ZMean;
        var zs = re.ZStdDev;
        var rm = re.rotationMean;
        var rs = re.rotationStdDev;
        var wd = re.poseEstimation.distanceWeight;
        var wr = re.poseEstimation.rotationWeight;
        return $"{xm},{xs},{zm},{zs},{rm},{rs},{wd},{wr}";
    }

    #endregion

    #region Actions

    void OnNewLine()
    {
        // add line end
        WriteLine("");
        AppendString(RowHeader());
    }

    void OnAccuracyAvailable()
    {
        var acc = randomizedEvaluation.GetAccuracy();
        if (acc < 0)
        {
            // invalid data
            return;
        }

        AppendString($",{acc}");
    }

    void OnLayoutGenerated()
    {
        // Save to images
        if (layoutPicture)
        {
            var filename = ScreenshotFilename();
            var path = Path.Combine(folder, filename);
            ScreenCapture.CaptureScreenshot(path);
        }
    }

    #endregion

    /// <summary>
    /// Close writer when app quits
    /// </summary>
    private void OnApplicationQuit()
    {
        CloseWriter();
    }

    #region Auto-naming

    /// <summary>
    /// Scene name for auto naming output file
    /// </summary>
    /// <returns></returns>
    public string GetSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    /// <summary>
    /// CSV filename, not the full path
    /// </summary>
    /// <returns></returns>
    string CSVFilename()
    {
        return $"{GetSceneName()}.csv";
    }

    /// <summary>
    /// Screenshot filename, not the full path
    /// </summary>
    /// <returns></returns>
    string ScreenshotFilename()
    {
        var re = randomizedEvaluation;
        var xs = re.XStdDev;
        var zs = re.ZStdDev;
        var rs = re.rotationStdDev;
        var wd = re.poseEstimation.distanceWeight;
        var wr = re.poseEstimation.rotationWeight;
        var s = $"{GetSceneName()}-layout-xs{xs}-zs{zs}-rs{rs}-wd{wd}-wr{wr}";
        s = s.Replace(".", "_"); // replace float 0.x with 0_x
        return $"{s}.png";
    }

    #endregion
}