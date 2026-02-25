using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

namespace Hierarchy2
{
    [InitializeOnLoad]
    public class ScriptToGameObjectAutoCreator
    {
        static ScriptToGameObjectAutoCreator()
        {
            // ヒエラルキーウィンドウのGUIイベントにフックする
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            // ドラッグ＆ドロップのイベントを取得
            Event currentEvent = Event.current;

            // ドラッグされているオブジェクトの中にMonoScriptがあるかチェック
            var draggedScripts = DragAndDrop.objectReferences
                .OfType<MonoScript>()
                .Where(s => s.GetClass() != null && s.GetClass().IsSubclassOf(typeof(MonoBehaviour)))
                .ToList();

            if (draggedScripts.Count == 0)
            {
                return;
            }

            switch (currentEvent.type)
            {
                case EventType.DragUpdated:
                    // ドラッグ中の表示を「コピー（＋）」に変更する
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    currentEvent.Use();
                    break;

                case EventType.DragPerform:
                    // ドロップが確定した瞬間の処理
                    DragAndDrop.AcceptDrag();

                    foreach (var script in draggedScripts)
                    {
                        CreateGameObjectFromScript(script);
                    }

                    currentEvent.Use();
                    break;
            }
        }

        private static void CreateGameObjectFromScript(MonoScript script)
        {
            Type scriptType = script.GetClass();

            if (scriptType != null)
            {
                // 1. 新しいGameObjectを作成（スクリプト名と同じ名前にする）
                GameObject go = new GameObject(scriptType.Name);

                // 2. スクリプトをコンポーネントとして追加
                go.AddComponent(scriptType);

                // 3. UnityのUndoシステムに登録（Ctrl+Zで消せるようにする）
                Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);

                // 4. 作成したオブジェクトを選択状態にする
                Selection.activeGameObject = go;

                Debug.Log($"[AutoCreator] Created GameObject with: {scriptType.Name}");
            }
        }
    }
}
