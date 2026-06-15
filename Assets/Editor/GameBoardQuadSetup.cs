#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 활성 씬의 게임 구성 오브젝트(루트)를 한데 묶어 4세트 복제하고,
/// 중심을 기준으로 상·좌·우·하 십자(+) 형태로 배치합니다.
/// (메인 카메라, 방향광, 이벤트 시스템, 글로벌 볼륨 등은 공용으로 두고 제외)
/// </summary>
public static class GameBoardQuadSetup
{
    const string MenuPath = "Tools/Streams/게임판 4개 배치 (십자·마주보기)";

    /// <summary>중심에서 각 세트까지의 거리(월드 단위). 기존 42의 약 5배.</summary>
    const float BoardSpacing = 210f;

    [MenuItem(MenuPath, false, 10)]
    public static void ArrangeFourBoards()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("플레이 모드에서는 실행할 수 없습니다.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogError("유효한 활성 씬이 없습니다.");
            return;
        }

        var existing = GameObject.Find("GameBoards");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog(
                    "GameBoards 존재",
                    "씬에 'GameBoards' 오브젝트가 이미 있습니다. 삭제한 뒤 다시 배치할까요?",
                    "삭제 후 진행",
                    "취소"))
                return;

            Undo.DestroyObjectImmediate(existing);
        }

        var roots = CollectAllGameRoots(scene);
        if (roots.Count == 0)
        {
            Debug.LogError("복제할 게임 루트가 없습니다. (Main Camera, Directional Light, EventSystem, Global Volume 등은 제외됩니다.)");
            return;
        }

        Undo.IncrementCurrentGroup();

        var gameBoards = new GameObject("GameBoards");
        Undo.RegisterCreatedObjectUndo(gameBoards, "Create GameBoards");

        var board0 = new GameObject("GameBoard_0");
        Undo.RegisterCreatedObjectUndo(board0, "Create GameBoard_0");
        board0.transform.SetParent(gameBoards.transform, false);

        foreach (var t in roots.OrderBy(r => r.name))
        {
            // RecordObject만 쓰면 부모 복원 순서가 꼬여 Ctrl+Z 시 자식 씬 오브젝트가 통째로 사라지는 경우가 있음
            Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "Parent to GameBoard_0");
            t.SetParent(board0.transform, true);
        }

        // 십자 배치 (위에서 보면): 북 / 서 / 동 / 남 — 가운데는 비움
        var poses = GetCrossTransforms(BoardSpacing);
        ApplyBoardTransform(board0.transform, poses[0].position, poses[0].rotation);

        for (int i = 1; i < 4; i++)
        {
            var clone = Object.Instantiate(board0, gameBoards.transform);
            clone.name = "GameBoard_" + i;
            Undo.RegisterCreatedObjectUndo(clone, "Duplicate Game Board");
            ApplyBoardTransform(clone.transform, poses[i].position, poses[i].rotation);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.SetCurrentGroupName("게임판 4개 십자 배치");

        Debug.Log(
            $"게임 루트 {roots.Count}개를 GameBoards 아래에 묶고 4세트를 십자 배치했습니다. (간격 {BoardSpacing})");
    }

    [MenuItem(MenuPath, true)]
    static bool ValidateMenu()
    {
        if (Application.isPlaying) return false;
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return false;
        // 이미 배치한 뒤에는 루트가 비어 있어도 메뉴로 다시 실행(삭제 대화) 가능해야 함
        if (GameObject.Find("GameBoards") != null) return true;
        return CollectAllGameRoots(scene).Count > 0;
    }

    /// <summary>
    /// 씬 최상위에서 게임에 해당하는 루트만 모읍니다. 공용 시스템 오브젝트는 제외합니다.
    /// </summary>
    static HashSet<Transform> CollectAllGameRoots(Scene scene)
    {
        var roots = new HashSet<Transform>();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == null) continue;
            if (ShouldExcludeFromGameDuplicate(go)) continue;
            roots.Add(go.transform);
        }

        return roots;
    }

    static bool ShouldExcludeFromGameDuplicate(GameObject go)
    {
        if (go.name == "GameBoards") return true;

        if (go.GetComponent<EventSystem>() != null) return true;

        if (go.GetComponent<Camera>() != null) return true;

        var light = go.GetComponent<Light>();
        if (light != null && light.type == LightType.Directional) return true;

        if (go.name == "Global Volume") return true;

        // URP/HDRP 등에서 흔한 이름
        if (go.name == "Post-process Volume" || go.name == "Post Process Volume") return true;

        // 리플렉션 프로브는 씬당 하나 두는 경우가 많아 공용으로 둠
        if (go.GetComponent<ReflectionProbe>() != null) return true;

        return false;
    }

    /// <summary>
    /// 위에서 본 십자(+) — 중심 (0,0,0) 을 향해 회전.
    /// [0] 북(앞/+Z 쪽) [1] 서(왼/-X) [2] 동(오른/+X) [3] 남(뒤/-Z)
    /// </summary>
    static (Vector3 position, Quaternion rotation)[] GetCrossTransforms(float d)
    {
        return new (Vector3, Quaternion)[]
        {
            (new Vector3(0f, 0f, d), Quaternion.Euler(0f, 180f, 0f)),
            (new Vector3(-d, 0f, 0f), Quaternion.Euler(0f, 90f, 0f)),
            (new Vector3(d, 0f, 0f), Quaternion.Euler(0f, -90f, 0f)),
            (new Vector3(0f, 0f, -d), Quaternion.Euler(0f, 0f, 0f)),
        };
    }

    static void ApplyBoardTransform(Transform board, Vector3 worldPos, Quaternion worldRot)
    {
        Undo.RegisterFullObjectHierarchyUndo(board.gameObject, "Move Game Board");
        board.SetPositionAndRotation(worldPos, worldRot);
    }
}
#endif
