#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// StreamsGameFlowController에 연결할 카메라 뷰(빈 Transform)를 생성·배치하고 인스펙터에 넣어 줍니다.
/// </summary>
public static class StreamsCameraViewsSetup
{
    const string MenuPath = "Tools/Streams/카메라 뷰 생성 및 Flow 연결";

    /// <summary>탑다운 시 보드에서 카메라까지 최소 거리 (StreamsBoardCameraPose와 동일 계산).</summary>
    const float DefaultHeight = 38f;

    [MenuItem(MenuPath, false, 20)]
    static void CreateAndWire()
    {
        var flow = Object.FindFirstObjectByType<StreamsGameFlowController>(FindObjectsInactive.Include);
        if (flow == null)
        {
            EditorUtility.DisplayDialog(
                "StreamsGameFlowController 없음",
                "씬에 StreamsGameFlowController 컴포넌트가 있는 오브젝트를 먼저 추가하고,\n최소한 Player Board(num_path)까지 연결해 주세요.",
                "확인");
            return;
        }

        if (flow.playerBoard == null)
        {
            EditorUtility.DisplayDialog("playerBoard 없음", "StreamsGameFlowController의 Player Board를 먼저 연결해 주세요.", "확인");
            return;
        }

        Undo.SetCurrentGroupName("카메라 뷰 생성");
        int group = Undo.GetCurrentGroup();

        var rig = GetOrCreateRig();

        var playerView = CreateOrUpdateViewChild(
            rig.transform,
            "PlayerCameraView",
            flow.playerBoard.transform);

        var aiTransforms = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            num_path aiBoard = (flow.aiBoards != null && i < flow.aiBoards.Length) ? flow.aiBoards[i] : null;
            string childName = $"AICameraView_{i}";
            if (aiBoard != null)
                aiTransforms[i] = CreateOrUpdateViewChild(rig.transform, childName, aiBoard.transform);
            else
                aiTransforms[i] = CreateOrUpdateEmptyChild(rig.transform, childName, Vector3.zero, Quaternion.identity);
        }

        using (var so = new SerializedObject(flow))
        {
            var cam = flow.mainCamera != null
                ? flow.mainCamera
                : (Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>());
            if (cam != null)
                so.FindProperty("mainCamera").objectReferenceValue = cam;

            so.FindProperty("playerCameraView").objectReferenceValue = playerView;
            var arr = so.FindProperty("aiCameraViews");
            if (arr != null && arr.isArray)
            {
                arr.arraySize = 3;
                for (int i = 0; i < 3; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = aiTransforms[i];
            }

            so.ApplyModifiedProperties();
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Selection.activeGameObject = rig;
        Debug.Log(
            "[Streams] StreamsCameraRig 아래에 뷰 4개를 두고 StreamsGameFlowController에 연결했습니다. 거리·높이는 스크립트 상단 상수로 조정 가능합니다.");
    }

    [MenuItem(MenuPath, true)]
    static bool ValidateMenu()
    {
        return !Application.isPlaying
               && Object.FindFirstObjectByType<StreamsGameFlowController>(FindObjectsInactive.Include) != null;
    }

    static GameObject GetOrCreateRig()
    {
        const string rigName = "StreamsCameraRig";
        var existing = GameObject.Find(rigName);
        if (existing != null)
            return existing;

        var go = new GameObject(rigName);
        Undo.RegisterCreatedObjectUndo(go, rigName);
        return go;
    }

    static Transform CreateOrUpdateViewChild(Transform rig, string childName, Transform board)
    {
        Transform tr = rig.Find(childName);
        GameObject go;
        if (tr != null)
        {
            go = tr.gameObject;
            Undo.RecordObject(go.transform, "Camera view pose");
        }
        else
        {
            go = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(go, childName);
            go.transform.SetParent(rig, false);
        }

        const float extentScale = 1.15f;
        var np = board.GetComponent<num_path>();
        IList<Slot3D> slotList = np != null ? np.slots : null;
        StreamsBoardCameraPose.GetCameraPose(
            board,
            slotList,
            Vector3.zero,
            new Vector3(90f, 0f, 0f),
            DefaultHeight,
            extentScale,
            out Vector3 camPos,
            out Quaternion rot);
        go.transform.SetPositionAndRotation(camPos, rot);
        return go.transform;
    }

    static Transform CreateOrUpdateEmptyChild(Transform rig, string childName, Vector3 localPos, Quaternion localRot)
    {
        Transform tr = rig.Find(childName);
        GameObject go;
        if (tr != null)
        {
            go = tr.gameObject;
            Undo.RecordObject(go.transform, "Camera view");
        }
        else
        {
            go = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(go, childName);
            go.transform.SetParent(rig, false);
        }

        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        return go.transform;
    }
}
#endif
