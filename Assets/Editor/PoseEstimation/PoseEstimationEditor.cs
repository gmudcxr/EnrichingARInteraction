using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PoseEstimation))]
public class PoseEstimationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // base.OnInspectorGUI();
        PoseEstimation poseEstimation = (PoseEstimation)target;

        if (DrawDefaultInspector())
        {
            // call
        }

        if (GUILayout.Button("Generate Graph"))
        {
            // call
            poseEstimation.CreateVoronoiGraph();
        }

        if (GUILayout.Button("Show Player Orientation"))
        {
            poseEstimation.ShowOrientation();
        }

        if (GUILayout.Button("Show Player FOV"))
        {
            poseEstimation.ShowCameraFOV();
        }

        if (GUILayout.Button("Show GroundTruth Label"))
        {
            poseEstimation.ShowGroundTruthLabel();
        }
        
        if (GUILayout.Button("Run"))
        {
            // call
            poseEstimation.Run();
        }
    }
}
