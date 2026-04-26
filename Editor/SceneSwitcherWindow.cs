using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace Hierarchy2
{
    /// <summary>
    /// シーンを簡単に切り替えるためのエディタウィンドウ
    /// </summary>
    public class SceneSwitcherWindow : EditorWindow
    {
        // 登録されたシーンパスのリスト
        private List<string> scenePaths = new List<string>();

        // 編集モードフラグ
        private bool isEditMode = false;

        // スクロール位置
        private Vector2 scrollPosition;

        // 保存用のEditorPrefsキー
        private const string PREFS_KEY = "Hierarchy2_SceneSwitcher_Paths";

        // ReorderableListのインスタンス
        private ReorderableList reorderableList;

        // 削除予定のインデックス
        private int itemToRemoveIndex = -1;

        [MenuItem("Tools/Hierarchy2/Scene Switcher")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneSwitcherWindow>("Scene Switcher");
            window.Show();
        }

        private void OnEnable()
        {
            LoadScenes();
            InitReorderableList();
        }

        private void OnDisable()
        {
            SaveScenes();
        }

        /// <summary>
        /// EditorPrefsからシーンリストを読み込む
        /// </summary>
        private void LoadScenes()
        {
            scenePaths.Clear();
            string data = EditorPrefs.GetString(PREFS_KEY, "");
            if (!string.IsNullOrEmpty(data))
            {
                var loaded = data.Split(';').Where(s => !string.IsNullOrEmpty(s));
                scenePaths.AddRange(loaded);
            }
        }

        /// <summary>
        /// EditorPrefsにシーンリストを保存する
        /// </summary>
        private void SaveScenes()
        {
            string data = string.Join(";", scenePaths);
            EditorPrefs.SetString(PREFS_KEY, data);
        }

        /// <summary>
        /// ReorderableListの初期化
        /// </summary>
        private void InitReorderableList()
        {
            // 並び替え可能、追加ボタン・削除ボタンは自前で処理するため非表示に設定
            reorderableList = new ReorderableList(scenePaths, typeof(string), true, true, false, false);

            reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                GUI.Label(rect, "登録済みのシーン");
            };

            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= scenePaths.Count) return;

                string path = scenePaths[index];
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                rect.y += 2;

                // バツボタンの幅
                float buttonWidth = 25f;
                Rect labelRect = new Rect(rect.x, rect.y, rect.width - buttonWidth - 5, EditorGUIUtility.singleLineHeight);
                Rect buttonRect = new Rect(rect.x + rect.width - buttonWidth, rect.y, buttonWidth, EditorGUIUtility.singleLineHeight);

                GUI.Label(labelRect, sceneName);

                if (GUI.Button(buttonRect, "X"))
                {
                    itemToRemoveIndex = index;
                }
            };

            reorderableList.onReorderCallback = (ReorderableList list) =>
            {
                SaveScenes();
            };
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            isEditMode = GUILayout.Toggle(isEditMode, "編集モード", "Button");
            GUILayout.EndHorizontal();

            // ウィンドウ全体へのドラッグ＆ドロップ対応（シーン登録）
            HandleDragAndDrop();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            if (isEditMode)
            {
                // 編集モード：ReorderableListを表示（並び替えと削除）
                itemToRemoveIndex = -1;

                if (reorderableList != null)
                {
                    reorderableList.DoLayoutList();
                }

                // 描画後にアイテムを削除する（エラー回避のため）
                if (itemToRemoveIndex >= 0 && itemToRemoveIndex < scenePaths.Count)
                {
                    scenePaths.RemoveAt(itemToRemoveIndex);
                    SaveScenes();
                    itemToRemoveIndex = -1;
                }
            }
            else
            {
                // 通常モード：クリックでシーン切り替えのボタンリスト
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    string path = scenePaths[i];
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                    if (GUILayout.Button(sceneName, GUILayout.Height(30)))
                    {
                        SwitchScene(path);
                    }
                }

                if (scenePaths.Count == 0)
                {
                    GUILayout.Label("シーンファイルをここにドラッグ＆ドロップして登録してください", EditorStyles.centeredGreyMiniLabel);
                }
            }

            GUILayout.EndScrollView();
        }

        /// <summary>
        /// シーンの切り替え処理
        /// </summary>
        private void SwitchScene(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                try
                {
                    EditorSceneManager.OpenScene(path);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("シーンの読み込みに失敗しました: " + e.Message);
                }
            }
        }

        /// <summary>
        /// シーンファイルをウィンドウにドラッグ＆ドロップして登録する処理
        /// </summary>
        private void HandleDragAndDrop()
        {
            Event evt = Event.current;
            Rect dropArea = new Rect(0, 0, position.width, position.height);

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        return;

                    // ドラッグされているものがシーンアセットかチェック
                    bool hasScene = DragAndDrop.paths.Any(p => p.EndsWith(".unity"));
                    if (!hasScene)
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (string path in DragAndDrop.paths)
                        {
                            if (path.EndsWith(".unity") && !scenePaths.Contains(path))
                            {
                                scenePaths.Add(path);
                            }
                        }
                        SaveScenes();
                    }
                    evt.Use();
                    break;
            }
        }
    }
}
