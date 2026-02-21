using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEditorInternal;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Hierarchy2
{
    /// <summary>
    /// Hierarchyウィンドウの表示をカスタマイズするためのエディタ拡張クラス
    /// </summary>
    [InitializeOnLoad]
    public sealed class HierarchyEditor
    {
        // グローバルな左側のオフセット値
        internal const int GLOBAL_SPACE_OFFSET_LEFT = 16 * 2;

        // シングルトンインスタンス
        static HierarchyEditor instance;

        /// <summary>
        /// シングルトンインスタンスへのアクセサ
        /// </summary>
        public static HierarchyEditor Instance
        {
            get
            {
                if (instance == null)
                    instance = new HierarchyEditor();
                return instance;
            }
            private set { instance = value; }
        }

        // 選択されたコンポーネントを保持する辞書
        Dictionary<int, UnityEngine.Object> selectedComponents = new Dictionary<int, UnityEngine.Object>();
        // 表示対象のコンポーネント名を保持する辞書
        Dictionary<string, string> dicComponents = new Dictionary<string, string>(StringComparer.Ordinal);
        // 現在アクティブなコンポーネント
        UnityEngine.Object activeComponent;

        // ツールチップ表示用のGUIContent
        GUIContent tooltipContent = new GUIContent();

        // Hierarchy設定
        HierarchySettings settings;
        // Hierarchyリソース（アイコンなど）
        HierarchyResources resources;

        // 使用中のテーマデータ
        HierarchySettings.ThemeData ThemeData
        {
            get { return settings.usedTheme; }
        }

        // 最も深い行のインデックス
        int deepestRow = int.MinValue;
        // 前回の行インデックス
        int previousRowIndex = int.MinValue;

        // シーンのインデックス
        int sceneIndex = 0;
        // 現在のシーン
        Scene currentScene;
        // 前回のシーン
        Scene previousScene;

        // マルチシーン編集モードかどうか
        public static bool IsMultiScene
        {
            get { return SceneManager.sceneCount > 1; }
        }

        // Invoke後に選択スタイルを適用したかどうかのフラグ
        bool selectionStyleAfterInvoke = false;
        // すべてのHierarchyウィンドウをチェック中かどうかのフラグ
        bool checkingAllHierarchy = false;

        // 現在のイベント
        Event currentEvent;

        // 現在処理中の行アイテムデータ
        RowItem rowItem = new RowItem();
        // ひとつ前の要素
        RowItem previousElement = null;
        // 横幅の使用状況
        WidthUse widthUse = WidthUse.zero;

        // 静的コンストラクタ。インスタンスを初期化する
        static HierarchyEditor()
        {
            if (instance == null)
                instance = new HierarchyEditor();
        }

        // コンストラクタ
        public HierarchyEditor()
        {
            // リフレクションによる内部フィールド等の取得
            InternalReflection();
            // エディタ起動時の更新イベント登録
            EditorApplication.update += EditorAwake;
            // パッケージインポート完了時のイベント登録
            AssetDatabase.importPackageCompleted += ImportPackageCompleted;
        }

        // 内部エディタ型のリスト
        static List<Type> InternalEditorType = new List<Type>();
        static Dictionary<string, Type> dicInternalEditorType = new Dictionary<string, Type>();
        // Hierarchyに表示するスクリプト型のリスト
        static List<Type> DisplayOnHierarchyScriptType = new List<Type>();
        static Dictionary<string, Type> dicDisplayOnHierarchyScriptType = new Dictionary<string, Type>();

        // Unity内部のエディタウィンドウ型
        static Type SceneHierarchyWindow;
        static Type SceneHierarchy;
        static Type GameObjectTreeViewGUI;

        // リフレクションで取得するフィールド情報
        static FieldInfo m_SceneHierarchy;
        static FieldInfo m_TreeView;
        static PropertyInfo gui;
        static FieldInfo k_IconWidth;

        // リフレクションで取得するメソッドのデリゲート
        static Func<SearchableEditorWindow> lastInteractedHierarchyWindowDelegate;
        static Func<IEnumerable> GetAllSceneHierarchyWindowsDelegate;
        static Func<GameObject, Rect, bool, bool> IconSelectorShowAtPositionDelegate;
        static Action<Rect, UnityEngine.Object, int> DisplayObjectContextMenuDelegate;

        // Hierarchyウィンドウ再描画時のコールバック
        public static Action OnRepaintHierarchyWindowCallback;
        // ウィンドウの並び順変更時のコールバック
        public static Action OnWindowsReorderedCallback;

        /// <summary>
        /// リフレクションを使用してUnity内部の非公開フィールドやメソッドを取得・初期化する
        /// </summary>
        static void InternalReflection()
        {
            // Editorアセンブリ内の全型を取得
            var arrayInteralEditorType = typeof(Editor).Assembly.GetTypes();
            InternalEditorType = arrayInteralEditorType.ToList();
            dicInternalEditorType = arrayInteralEditorType.ToDictionary(type => type.FullName);

            // Hierarchy更新デリゲートのフック
            FieldInfo refreshHierarchy = typeof(EditorApplication).GetField(nameof(refreshHierarchy), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo OnRepaintHierarchyWindow = typeof(HierarchyEditor).GetMethod(nameof(OnRepaintHierarchyWindow), BindingFlags.NonPublic | BindingFlags.Static);
            Delegate refreshHierarchyDelegate = Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), OnRepaintHierarchyWindow);
            refreshHierarchy.SetValue(null, refreshHierarchyDelegate);

            // ウィンドウ並び替えデリゲートのフック
            FieldInfo windowsReordered = typeof(EditorApplication).GetField(nameof(windowsReordered), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo OnWindowsReordered = typeof(HierarchyEditor).GetMethod(nameof(OnWindowsReordered), BindingFlags.NonPublic | BindingFlags.Static);
            Delegate windowsReorderedDelegate = Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), OnWindowsReordered);
            windowsReordered.SetValue(null, windowsReorderedDelegate);

            // 必要な内部型の取得
            {
                dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(SceneHierarchyWindow), out SceneHierarchyWindow);
                dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(GameObjectTreeViewGUI), out GameObjectTreeViewGUI); //GameObjectTreeViewGUI : TreeViewGUI
                dicInternalEditorType.TryGetValue(nameof(UnityEditor) + "." + nameof(SceneHierarchy), out SceneHierarchy);
            }

            // 最後に操作されたHierarchyウィンドウを取得するメソッド
            FieldInfo s_LastInteractedHierarchy = SceneHierarchyWindow.GetField(nameof(s_LastInteractedHierarchy), BindingFlags.NonPublic | BindingFlags.Static);

            MethodInfo lastInteractedHierarchyWindow = SceneHierarchyWindow.GetProperty(nameof(lastInteractedHierarchyWindow), BindingFlags.Static | BindingFlags.Public).GetGetMethod();
            lastInteractedHierarchyWindowDelegate = Delegate.CreateDelegate(typeof(Func<SearchableEditorWindow>), lastInteractedHierarchyWindow) as Func<SearchableEditorWindow>;

            // 全てのHierarchyウィンドウを取得するメソッド
            MethodInfo GetAllSceneHierarchyWindows = SceneHierarchyWindow.GetMethod(nameof(GetAllSceneHierarchyWindows), BindingFlags.Static | BindingFlags.Public);
            GetAllSceneHierarchyWindowsDelegate = Delegate.CreateDelegate(typeof(Func<IEnumerable>), GetAllSceneHierarchyWindows) as Func<IEnumerable>;

            // 内部フィールドの取得
            {
                m_SceneHierarchy = SceneHierarchyWindow.GetField(nameof(m_SceneHierarchy), BindingFlags.NonPublic | BindingFlags.Instance);
                m_TreeView = SceneHierarchy.GetField(nameof(m_TreeView), BindingFlags.NonPublic | BindingFlags.Instance);
                gui = m_TreeView.FieldType.GetProperty(nameof(gui).ToLower(), BindingFlags.Public | BindingFlags.Instance);
                k_IconWidth = GameObjectTreeViewGUI.GetField(nameof(k_IconWidth), BindingFlags.Public | BindingFlags.Instance);
            }

            // オブジェクトのコンテキストメニュー表示メソッド
            MethodInfo DisplayObjectContextMenu = typeof(EditorUtility).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single
            (
                method => method.Name == nameof(DisplayObjectContextMenu) && method.GetParameters()[1].ParameterType == typeof(UnityEngine.Object)
            );
            DisplayObjectContextMenuDelegate = Delegate.CreateDelegate(typeof(Action<Rect, UnityEngine.Object, int>), DisplayObjectContextMenu) as Action<Rect, UnityEngine.Object, int>;

            // アイコンセレクター表示メソッド
            Type IconSelector = typeof(EditorWindow).Assembly.GetTypes().Single(type =>
                type.BaseType == typeof(EditorWindow) && type.Name == nameof(IconSelector)) as Type;
            MethodInfo ShowAtPosition = IconSelector.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single
            (
                method => method.Name == nameof(ShowAtPosition) &&
                          method.GetParameters()[0].ParameterType == typeof(UnityEngine.Object)
            );
            IconSelectorShowAtPositionDelegate = Delegate.CreateDelegate(typeof(Func<GameObject, Rect, bool, bool>), ShowAtPosition) as Func<GameObject, Rect, bool, bool>;

            // TreeView関連の内部メソッド・プロパティ取得
            GetItemAndRowIndexMethod = m_TreeView.FieldType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Single(method => method.Name == "GetItemAndRowIndex");

            m_TreeView_IData = m_TreeView.FieldType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Single(property => property.Name == "data");

            m_Rows = InternalEditorType.Find(type => type.Name == "TreeViewDataSource").GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(field => field.Name.Contains(nameof(m_Rows)));
        }

        // 全てのシーン階層ウィンドウを取得する
        public static IEnumerable GetAllSceneHierarchyWindows() => GetAllSceneHierarchyWindowsDelegate();

        // オブジェクトのコンテキストメニューを表示する
        public static void DisplayObjectContextMenu(Rect rect, UnityEngine.Object unityObject, int value) => DisplayObjectContextMenuDelegate(rect, unityObject, value);

        // アイコンセレクターを指定位置に表示する
        public static bool IconSelectorShowAtPosition(GameObject gameObject, Rect rect, bool value) => IconSelectorShowAtPositionDelegate(gameObject, rect, value);

        private static MethodInfo GetItemAndRowIndexMethod;
        private static PropertyInfo m_TreeView_IData;
        private static FieldInfo m_Rows;

        // Hierarchyウィンドウ再描画時の処理
        static void OnRepaintHierarchyWindow()
        {
            OnRepaintHierarchyWindowCallback?.Invoke();
        }

        // ウィンドウ並び替え時の処理
        static void OnWindowsReordered()
        {
            OnWindowsReorderedCallback?.Invoke();
        }

        // エディタ起動時の初期化処理
        void EditorAwake()
        {
            // 設定とリソースの読み込み
            settings = HierarchySettings.GetAssets();
            if (settings is null) return;
            OnSettingsChanged(nameof(settings.components));
            settings.onSettingsChanged += OnSettingsChanged;

            resources = HierarchyResources.GetAssets();
            if (resources is null) return;
            resources.GenerateKeyForAssets();

            // HierarchyのGUIイベント登録
            EditorApplication.hierarchyWindowItemOnGUI += HierarchyOnGUI;

            // 設定に応じて有効化/無効化
            if (settings.activeHierarchy)
                Invoke();
            else
                Dispose();

            EditorApplication.update -= EditorAwake;
        }

        // パッケージインポート完了時
        void ImportPackageCompleted(string packageName)
        {
        }

        // 設定変更時のコールバック
        void OnSettingsChanged(string param)
        {
            switch (param)
            {
                case nameof(settings.components):
                    // 表示コンポーネントのリストを更新
                    dicComponents.Clear();
                    foreach (string componentType in settings.components)
                    {
                        if (!dicComponents.ContainsKey(componentType))
                            dicComponents.Add(componentType, componentType);
                    }

                    break;
            }

            EditorApplication.RepaintHierarchyWindow();
        }

        // 機能を有効化し、各種イベントハンドラを登録する
        public void Invoke()
        {
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.sceneLoaded += OnSceneLoaded;
            EditorSceneManager.sceneUnloaded += OnSceneUnloaded;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneDirtied += OnSceneDirtied;

            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.modifierKeysChanged += OnModifierKeysChanged;

            PrefabUtility.prefabInstanceUpdated += OnPrefabUpdated;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            EditorApplication.update += OnEditorUpdate;

            selectionStyleAfterInvoke = false;
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        // 機能を無効化し、イベントハンドラを解除する
        public void Dispose()
        {
            EditorSceneManager.newSceneCreated -= OnNewSceneCreated;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneLoaded -= OnSceneLoaded;
            EditorSceneManager.sceneUnloaded -= OnSceneUnloaded;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneDirtied -= OnSceneDirtied;

            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.modifierKeysChanged -= OnModifierKeysChanged;

            PrefabUtility.prefabInstanceUpdated -= OnPrefabUpdated;
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;

            EditorApplication.update -= OnEditorUpdate;

            // ウィンドウタイトルを元に戻す
            foreach (EditorWindow window in GetAllSceneHierarchyWindowsDelegate())
            {
                window.titleContent.text = "Hierarchy";
            }

            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }

        // 前回の更新時間
        double lastTimeSinceStartup = EditorApplication.timeSinceStartup;

        // エディタの更新ループ
        void OnEditorUpdate()
        {
            // 1秒ごとに遅延呼び出し処理を実行
            if (EditorApplication.timeSinceStartup - lastTimeSinceStartup >= 1)
            {
                DelayCall();
                lastTimeSinceStartup = EditorApplication.timeSinceStartup;
            }
        }

        // 定期的に実行される処理（ウィンドウの管理など）
        void DelayCall()
        {
            if (checkingAllHierarchy == true)
            {
                // 閉じたウィンドウの情報をクリーンアップ
                for (int i = 0; i < HierarchyWindow.windows.Count; ++i)
                {
                    if (HierarchyWindow.windows[i].editorWindow == null)
                    {
                        HierarchyWindow.windows[i].Dispose();
                        --i;
                    }
                }

                // 新しいHierarchyウィンドウを追跡対象に追加
                foreach (EditorWindow window in GetAllSceneHierarchyWindowsDelegate())
                {
                    if (!HierarchyWindow.instances.ContainsKey(window.GetInstanceID()))
                    {
                        var hierarchyWindow = new HierarchyWindow(window);
                        hierarchyWindow.SetWindowTitle("Hierarchy 2");
                    }
                }

                checkingAllHierarchy = false;
            }

            if (hierarchyChangedRequireUpdating == true)
            {
                hierarchyChangedRequireUpdating = false;
            }
        }

        void OnModifierKeysChanged()
        {
        }

        [DidReloadScripts]
        static void OnEditorCompiled()
        {
        }

        void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
        }

        void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (settings is null) return;
        }

        void OnSceneClosed(Scene scene)
        {
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
        }

        void OnSceneUnloaded(Scene scene)
        {
        }

        void OnSceneSaved(Scene scene)
        {
        }

        void OnSceneDirtied(Scene scene)
        {
        }

        bool hierarchyChangedRequireUpdating = false;

        void OnHierarchyChanged()
        {
            hierarchyChangedRequireUpdating = true;
        }

        void OnPrefabUpdated(GameObject prefab)
        {
        }

        bool prefabStageChanged = false;

        void OnPrefabStageOpened(PrefabStage stage)
        {
            prefabStageChanged = true;
        }

        void OnPrefabStageClosing(PrefabStage stage)
        {
            prefabStageChanged = true;

            for (int i = 0; i < HierarchyWindow.windows.Count; ++i)
            {
                HierarchyWindow.windows[i].Reflection();
            }
        }

        // Hierarchyの各アイテム描画時のGUI処理
        void HierarchyOnGUI(int selectionID, Rect selectionRect)
        {
            currentEvent = Event.current;

            // Ctrl+H で機能の有効/無効を切り替え
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.H && currentEvent.control)
            {
                if (!settings.activeHierarchy)
                    Invoke();
                else
                    Dispose();

                settings.activeHierarchy = !settings.activeHierarchy;
                currentEvent.Use();
            }

            if (!settings.activeHierarchy)
                return;

            // Ctrl+D (複製) の場合は処理しない
            if (currentEvent.control && currentEvent.keyCode == KeyCode.D)
                return;

            if (currentEvent.type == EventType.Layout)
            {
                if (prefabStageChanged)
                {
                    prefabStageChanged = false;
                }

                return;
            }

            checkingAllHierarchy = true;

            // Invoke後にマウスダウンがあった場合、選択スタイル適用済みとする
            if (selectionStyleAfterInvoke == false && currentEvent.type == EventType.MouseDown)
            {
                selectionStyleAfterInvoke = true;
            }

            // 行アイテム情報の更新
            rowItem.Dispose();
            rowItem.ID = selectionID;
            rowItem.gameObject = EditorUtility.InstanceIDToObject(rowItem.ID) as GameObject;
            rowItem.rect = selectionRect;
            rowItem.rowIndex = GetRowIndex(selectionRect);
            rowItem.isSelected = InSelection(selectionID);
            rowItem.isFirstRow = IsFirstRow(selectionRect);
            rowItem.isFirstElement = IsFirstElement(selectionRect);

            rowItem.isNull = rowItem.gameObject == null ? true : false;

            if (!rowItem.isNull)
            {
                rowItem.hierarchyFolder = rowItem.gameObject.GetComponent<HierarchyFolder>();
                // HierarchyFolderコンポーネントがあるか、またはセパレータとして扱うか
                if (!(rowItem.isFolder = rowItem.hierarchyFolder))
                    rowItem.isSeparator = rowItem.name.StartsWith(settings.separatorStartWith);

                rowItem.isDirty = EditorUtility.IsDirty(selectionID);

                // プレハブ判定
                if (true && !rowItem.isSeparator && rowItem.isDirty)
                {
                    rowItem.isPrefab = PrefabUtility.IsPartOfAnyPrefab(rowItem.gameObject);

                    if (rowItem.isPrefab)
                        rowItem.isPrefabMissing = PrefabUtility.IsPrefabAssetMissing(rowItem.gameObject);
                }
            }

            rowItem.isRootObject = rowItem.isNull || rowItem.gameObject.transform.parent == null ? true : false;
            rowItem.isMouseHovering = selectionRect.Contains(currentEvent.mousePosition);

            // 最初の行の処理（シーンヘッダなど）
            if (rowItem.isFirstRow) //Instance always null
            {
                sceneIndex = 0;

                if (deepestRow > previousRowIndex)
                    deepestRow = previousRowIndex;

                // バージョン表示や背景描画（コメントアウト中）
                // if (settings.displayVersion)
                //     BottomRightArea(selectionRect);

                // Background(selectionRect);
            }

            // GameObjectがnullの場合（シーンセパレータなど）
            if (rowItem.isNull)
            {
                if (!IsMultiScene)
                    currentScene = SceneManager.GetActiveScene();
                else
                {
                    // マルチシーンの場合、適切なシーンを取得
                    if (!rowItem.isFirstRow && sceneIndex < SceneManager.sceneCount - 1)
                        sceneIndex++;
                    currentScene = SceneManager.GetSceneAt(sceneIndex);
                }

                // シーン名のリネームUI
                RenameSceneInHierarchy();

                // 背景色描画
                if (settings.displayRowBackground)
                {
                    if (deepestRow != rowItem.rowIndex)
                        DisplayRowBackground();
                }

                previousElement = rowItem;
                previousRowIndex = rowItem.rowIndex;
                previousScene = currentScene;

                if (previousRowIndex > deepestRow)
                    deepestRow = previousRowIndex;
                return;
            }
            else
            {
                // 各GameObject行の描画
                if (rowItem.isFirstElement)
                {
                    if (deepestRow > previousRowIndex)
                        deepestRow = previousRowIndex;
                    deepestRow -= rowItem.rowIndex;

                    if (IsMultiScene)
                    {
                        if (!previousElement.isNull)
                        {
                            for (int i = 0; i < SceneManager.sceneCount; ++i)
                            {
                                if (SceneManager.GetSceneAt(i) == rowItem.gameObject.scene)
                                {
                                    sceneIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (IsMultiScene)
                {
                }

                rowItem.nameRect = rowItem.rect;
                GUIStyle nameStyle = TreeStyleFromFont(FontStyle.Normal);
                rowItem.nameRect.width = nameStyle.CalcSize(new GUIContent(rowItem.gameObject.name)).x;

                rowItem.nameRect.x += 16;

                var isPrefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null ? true : false;

                // 背景縞模様の描画
                if (settings.displayRowBackground && deepestRow != rowItem.rowIndex)
                {
                    if (isPrefabMode)
                    {
                        if (rowItem.gameObject.transform.parent == null) //Should use row index instead.
                        {
                            if (deepestRow != 0)
                                DisplayRowBackground();
                        }
                    }
                    else
                        DisplayRowBackground();
                }

                // フォルダアイコンの表示
                if (rowItem.isFolder)
                {
                    var icon = rowItem.childCount > 0 ? Resources.FolderIcon : Resources.EmptyFolderIcon;
                    DisplayCustomObjectIcon(icon);
                }

                // セパレータの描画
                if (rowItem.isSeparator && rowItem.isRootObject)
                {
                    ElementAsSeparator();
                    goto FINISH;
                }

                // 特定条件での背景色変更
                if (settings.useInstantBackground)
                    CustomRowBackground();

                // ツリービューの線を描画
                if (settings.displayTreeView && !rowItem.isRootObject)
                    DisplayTreeView();

                // カスタムアイコンの表示
                if (settings.displayCustomObjectIcon)
                    DisplayCustomObjectIcon(null);

                widthUse = WidthUse.zero;
                widthUse.left += GLOBAL_SPACE_OFFSET_LEFT;
                if (isPrefabMode) widthUse.left -= 2;
                widthUse.afterName = rowItem.nameRect.x + rowItem.nameRect.width;

                widthUse.afterName += settings.offSetIconAfterName;

                // 編集不可アイコンの表示
                DisplayEditableIcon();

                // DisplayNoteIcon();

                widthUse.afterName += 8;

                // タグの表示
                if (settings.displayTag && !rowItem.gameObject.CompareTag("Untagged"))
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Tag) !=
                        HierarchySettings.ContentDisplay.Tag ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Tag) ==
                            HierarchySettings.ContentDisplay.Tag && rowItem.isMouseHovering))
                    {
                        DisplayTag();
                    }
                }

                // レイヤーの表示
                if (settings.displayLayer && rowItem.gameObject.layer != 0)
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Layer) !=
                        HierarchySettings.ContentDisplay.Layer ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Layer) ==
                            HierarchySettings.ContentDisplay.Layer && rowItem.isMouseHovering))
                    {
                        DisplayLayer();
                    }
                }

                // コンポーネントアイコンの表示
                if (settings.displayComponents)
                {
                    if (!settings.onlyDisplayWhileMouseEnter ||
                        (settings.contentDisplay & HierarchySettings.ContentDisplay.Component) !=
                        HierarchySettings.ContentDisplay.Component ||
                        ((settings.contentDisplay & HierarchySettings.ContentDisplay.Component) ==
                            HierarchySettings.ContentDisplay.Component && rowItem.isMouseHovering))
                    {
                        DisplayComponents();
                    }
                }

                // その他の要素イベント処理
                ElementEvent(rowItem);

                FINISH:
                // グリッド線の表示
                if (settings.displayGrid)
                    DisplayGrid();

                previousElement = rowItem;
                previousRowIndex = rowItem.rowIndex;
                previousScene = currentScene;

                if (previousRowIndex > deepestRow)
                {
                    deepestRow = previousRowIndex;
                }
            }
        }

        // フォントスタイルからGUIStyleを取得
        GUIStyle TreeStyleFromFont(FontStyle fontStyle)
        {
            GUIStyle style;
            switch (fontStyle)
            {
                case FontStyle.Bold:
                    style = new GUIStyle(Styles.TreeBoldLabel);
                    break;

                case FontStyle.Italic:
                    style = new GUIStyle(Styles.TreeLabel);
                    break;

                case FontStyle.BoldAndItalic:
                    style = new GUIStyle(Styles.TreeBoldLabel);
                    break;

                default:
                    style = new GUIStyle(Styles.TreeLabel);
                    break;
            }

            return style;
        }

        // 条件に基づき行の背景色をカスタム描画する
        void CustomRowBackground()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            HierarchySettings.InstantBackgroundColor instantBackgroundColor = new HierarchySettings.InstantBackgroundColor();
            bool contain = false;
            for (int i = 0; i < settings.instantBackgroundColors.Count; ++i)
            {
                if (!settings.instantBackgroundColors[i].active) continue;
                if
                (
                    (settings.instantBackgroundColors[i].useTag && !string.IsNullOrEmpty(settings.instantBackgroundColors[i].tag) && rowItem.gameObject.CompareTag(settings.instantBackgroundColors[i].tag)) ||
                    (settings.instantBackgroundColors[i].useLayer && (1 << rowItem.gameObject.layer & settings.instantBackgroundColors[i].layer) != 0) ||
                    (settings.instantBackgroundColors[i].useStartWith && !string.IsNullOrEmpty(settings.instantBackgroundColors[i].startWith) && rowItem.name.StartsWith(settings.instantBackgroundColors[i].startWith))
                )
                {
                    contain = true;
                    instantBackgroundColor = settings.instantBackgroundColors[i];
                }
            }

            if (!contain) return;
            Color guiColor = GUI.color;
            GUI.color = instantBackgroundColor.color;
            Rect rect;
            var texture = Resources.PixelWhite;
            rect = RectFromRight(rowItem.rect, rowItem.rect.width + 16, 0);
            rect.x += 16;
            rect.xMin = 32;

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        // オブジェクトをセパレータとして描画する
        void ElementAsSeparator()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            if (!rowItem.gameObject.CompareTag(settings.separatorDefaultTag))
                rowItem.gameObject.tag = settings.separatorDefaultTag;

            var rect = EditorGUIUtility.PixelsToPoints(RectFromLeft(rowItem.rect, Screen.width, 0));
            rect.y = rowItem.rect.y;
            rect.height = rowItem.rect.height;
            rect.x += GLOBAL_SPACE_OFFSET_LEFT;
            rect.width -= GLOBAL_SPACE_OFFSET_LEFT;
            Color guiColor = GUI.color;
            GUI.color = ThemeData.colorHeaderBackground;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);

            var content = new GUIContent(rowItem.name.Remove(0, settings.separatorStartWith.Length));
            rect.x += (rect.width - Styles.Header.CalcSize(content).x) / 2;
            GUI.color = ThemeData.colorHeaderTitle;
            GUI.Label(rect, content, Styles.Header);
            GUI.color = guiColor;
        }

        // アイテムに対するキーイベントやマウスイベントの処理
        void ElementEvent(RowItem element)
        {
            if (currentEvent.type == EventType.KeyDown)
            {
                // Ctrl+Shift+Alt+C で全て折りたたむ
                if (currentEvent.control && currentEvent.shift && currentEvent.alt &&
                    currentEvent.keyCode == KeyCode.C && lastInteractedHierarchyWindowDelegate() != null)
                    CollapseAll();
            }

            // F2キーでリネームポップアップ表示
            if (currentEvent.type == EventType.KeyUp &&
                currentEvent.keyCode == KeyCode.F2 &&
                Selection.gameObjects.Length > 1)
            {
                var window = SelectionsRenamePopup.ShowPopup();
                currentEvent.Use();
                return;
            }

            // マウスホイールクリックでアクティブ状態の切り替え
            if (element.rect.Contains(currentEvent.mousePosition) && currentEvent.type == EventType.MouseUp &&
                currentEvent.button == 2)
            {
                Undo.RegisterCompleteObjectUndo(element.gameObject,
                    element.gameObject.activeSelf ? "Inactive object" : "Active object");
                element.gameObject.SetActive(!element.gameObject.activeSelf);
                currentEvent.Use();
                return;
            }
        }

        // Staticアイコンの表示と切り替え処理
        void StaticIcon(RowItem element)
        {
            if (!element.isStatic) return;

            var rect = element.rect;
            rect = RectFromRight(rect, 3, 0);

            if (currentEvent.type == EventType.MouseUp &&
                currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GenericMenu staticMenu = new GenericMenu();
                staticMenu.AddItem(new GUIContent("Apply All Children"), settings.applyStaticTargetAndChild,
                    () => { settings.applyStaticTargetAndChild = !settings.applyStaticTargetAndChild; });
                staticMenu.AddSeparator("");
                staticMenu.AddItem(new GUIContent("True"), element.gameObject.isStatic ? true : false,
                    () => { element.gameObject.isStatic = !element.gameObject.isStatic; });
                staticMenu.AddItem(new GUIContent("False"), !element.gameObject.isStatic ? true : false,
                    () => { element.gameObject.isStatic = !element.gameObject.isStatic; });
                staticMenu.ShowAsContext();
                currentEvent.Use();
            }

            GUISeparator(rect, Color.magenta);
        }

        // Staticフラグを子オブジェクトにも適用する再帰関数
        void ApplyStaticTargetAndChild(Transform target, bool value)
        {
            target.gameObject.isStatic = value;

            for (int i = 0; i < target.childCount; ++i)
                ApplyStaticTargetAndChild(target.GetChild(i), value);
        }

        // カスタムオブジェクトアイコンの表示
        void DisplayCustomObjectIcon(Texture icon)
        {
            var rect = RectFromRight(rowItem.nameRect, 16, rowItem.nameRect.width + 1);
            rect.height = 16;

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                IconSelectorShowAtPositionDelegate(rowItem.gameObject, rect, true);
                currentEvent.Use();
            }

            if (currentEvent.type == EventType.Repaint)
            {
                if (rect.Contains(currentEvent.mousePosition))
                {
                }

                if (icon == null)
                {
                    icon = AssetPreview.GetMiniThumbnail(rowItem.gameObject);
                    if (icon.name == "GameObject Icon" || icon.name == "d_GameObject Icon" || icon.name == "Prefab Icon" ||
                        icon.name == "d_Prefab Icon" || icon.name == "PrefabModel Icon" ||
                        icon.name == "d_PrefabModel Icon")
                        return;
                }

                Color guiColor = GUI.color;
                GUI.color = rowItem.rowIndex % 2 != 0 ? ThemeData.colorRowEven : ThemeData.colorRowOdd;
                GUI.DrawTexture(rect, Resources.PixelWhite);
                GUI.color = guiColor;
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
                //ReplaceObjectIcon(rowItem.ID, icon);
            }
        }

        // ロック（編集不可）アイコンの表示と解除メニュー
        void DisplayEditableIcon()
        {
            if (rowItem.gameObject.hideFlags == HideFlags.NotEditable)
            {
                Rect lockRect = RectFromLeft(rowItem.nameRect, 12, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUI.color = ThemeData.colorLockIcon;
                    GUI.DrawTexture(lockRect, Resources.lockIconOn, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }

                if (currentEvent.type == EventType.MouseUp &&
                    currentEvent.button == 1 &&
                    lockRect.Contains(currentEvent.mousePosition))
                {
                    GenericMenu lockMenu = new GenericMenu();

                    GameObject gameObject = rowItem.gameObject;

                    lockMenu.AddItem(new GUIContent("Unlock"), false, () =>
                    {
                        Undo.RegisterCompleteObjectUndo(gameObject, "Unlock...");
                        foreach (Component component in gameObject.GetComponents<Component>())
                        {
                            if (component)
                            {
                                Undo.RegisterCompleteObjectUndo(component, "Unlock...");
                                component.hideFlags = HideFlags.None;
                            }
                        }

                        gameObject.hideFlags = HideFlags.None;

                        InternalEditorUtility.RepaintAllViews();
                    });
                    lockMenu.ShowAsContext();
                    currentEvent.Use();
                }
            }
        }

        void DisplayNoteIcon()
        {
            // ノートアイコン表示機能（コメントアウト中）
            // if (!element.hasLocalData || element.data.note == "")
            //     return;

            // var iconRect = RectFromLeft(element.nameRect, 14, ref widthUse.afterName);
            // if (currentEvent.type == EventType.Repaint)
            // {
            //     GUIContent content = new GUIContent("", element.data.note);
            //     GUI.Box(iconRect, content, GUIStyle.none);
            //     GUI.color = Color.yellow;
            //     GUI.DrawTexture(iconRect, Resources.NoteIcon, ScaleMode.ScaleToFit);
            //     GUI.color = Color.white;
            // }

            // if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 && iconRect.Contains(currentEvent.mousePosition))
            // {
            //     GenericMenu noteMenu = new GenericMenu();
            //     noteMenu.AddItem(new GUIContent("Remove Note"), false, () =>
            //     {
            //         element.data.note = "";
            //     });
            //     noteMenu.ShowAsContext();
            //     currentEvent.Use();
            // }

            // widthUse.afterName += 2;
        }

        // コンポーネントアイコン一覧の表示
        void DisplayComponents()
        {
            var components = rowItem.gameObject.GetComponents(typeof(Component)).ToList<UnityEngine.Object>();
            var rendererComponent = rowItem.gameObject.GetComponent<Renderer>();
            bool hasMaterial = rendererComponent != null && rendererComponent.sharedMaterial != null;

            if (hasMaterial)
            {
                for (int i = 0; i < rendererComponent.sharedMaterials.Length; ++i)
                {
                    Material sharedMat = rendererComponent.sharedMaterials[i];
                    components.Add(sharedMat);
                }
            }

            int length = components.Count;
            bool separator = false;
            float widthUsedCached = 0;
            if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                widthUsedCached = widthUse.afterName;
                widthUse.afterName += 4;
            }
            else
            {
                widthUsedCached = widthUse.right;
                widthUse.right += 2;
            }

            for (int i = 0; i < length; ++i)
            {
                var component = components[i];

                try
                {
                    Type comType = component.GetType();

                    if (comType != null)
                    {
                        bool isMono = false;
                        if (comType.BaseType == typeof(MonoBehaviour)) isMono = true;
                        if (isMono)
                        {
                            //TODO: ???
                            bool shouldIgnoreThisMono = false;
                            if (shouldIgnoreThisMono) continue;
                        }

                        switch (settings.componentDisplayMode)
                        {
                            case HierarchySettings.ComponentDisplayMode.ScriptOnly:
                                if (!isMono)
                                    continue;
                                break;

                            case HierarchySettings.ComponentDisplayMode.Specified:
                                if (!dicComponents.ContainsKey(comType.Name))
                                    continue;
                                break;

                            case HierarchySettings.ComponentDisplayMode.Ignore:
                                if (dicComponents.ContainsKey(comType.Name))
                                    continue;
                                break;
                        }

                        Rect rect = Rect.zero;

                        if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                            rect = RectFromLeft(rowItem.nameRect, settings.componentSize, ref widthUse.afterName);
                        else
                            rect = RectFromRight(rowItem.rect, settings.componentSize, ref widthUse.right);


                        if (hasMaterial && i == length - rendererComponent.sharedMaterials.Length &&
                            settings.componentDisplayMode != HierarchySettings.ComponentDisplayMode.ScriptOnly)
                        {
                            for (int m = 0; m < rendererComponent.sharedMaterials.Length; ++m)
                            {
                                var sharedMaterial = rendererComponent.sharedMaterials[m];

                                if (sharedMaterial == null) continue;
                                ComponentIcon(sharedMaterial, comType, rect, true);

                                if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                                    rect = RectFromLeft(rowItem.nameRect, settings.componentSize,
                                        ref widthUse.afterName);
                                else
                                    rect = RectFromRight(rowItem.rect, settings.componentSize, ref widthUse.right);
                            }

                            separator = true;
                            break;
                        }

                        ComponentIcon(component, comType, rect);

                        if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                            widthUse.afterName += settings.componentSpacing;
                        else
                            widthUse.right += settings.componentSpacing;

                        separator = true;
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
            }

            if (separator && currentEvent.type == EventType.Repaint)
            {
                if (settings.componentAlignment == HierarchySettings.ElementAlignment.AfterName)
                    GUISeparator(RectFromLeft(rowItem.nameRect, 2, widthUsedCached), ThemeData.colorGrid);
                // else
                //     GUISeparator(RectFromRight(element.rect, 2, widthUsedCached), ThemeData.colorGrid);
            }
        }

        // 個別のコンポーネントアイコン描画とクリックイベント処理
        void ComponentIcon(UnityEngine.Object component, Type componentType, Rect rect, bool isMaterial = false)
        {
            int comHash = component.GetHashCode();

            if (currentEvent.type == EventType.Repaint)
            {
                Texture image = EditorGUIUtility.ObjectContent(component, componentType).image;

                if (selectedComponents.ContainsKey(comHash))
                {
                    Color guiColor = GUI.color;
                    GUI.color = ThemeData.comSelBGColor;
                    GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
                    GUI.color = guiColor;
                }

                string tooltip = isMaterial ? component.name : componentType.Name;
                tooltipContent.tooltip = tooltip;
                GUI.Box(rect, tooltipContent, GUIStyle.none);

                GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
            }


            if (rect.Contains(currentEvent.mousePosition))
            {
                if (currentEvent.type == EventType.MouseDown)
                {
                    // 左クリック：選択
                    if (currentEvent.button == 0)
                    {
                        if (currentEvent.control)
                        {
                            if (!selectedComponents.ContainsKey(comHash))
                            {
                                selectedComponents.Add(comHash, component);
                                activeComponent = component;
                            }
                            else
                            {
                                selectedComponents.Remove(comHash);
                            }

                            currentEvent.Use();
                            return;
                        }

                        selectedComponents.Clear();
                        selectedComponents.Add(comHash, component);
                        activeComponent = component;
                        currentEvent.Use();
                        return;
                    }

                    // 右クリック：コンテキストメニュー
                    if (currentEvent.button == 1)
                    {
                        if (currentEvent.control)
                        {
                            GenericMenu componentGenericMenu = new GenericMenu();

                            componentGenericMenu.AddItem(new GUIContent("Remove All Component"), false, () =>
                            {
                                if (!selectedComponents.ContainsKey(comHash))
                                    selectedComponents.Add(comHash, component);

                                foreach (var selectedComponent in selectedComponents.ToList())
                                {
                                    if (selectedComponent.Value is Material)
                                        continue;

                                    selectedComponents.Remove(selectedComponent.Key);
                                    Undo.DestroyObjectImmediate(selectedComponent.Value);
                                }

                                selectedComponents.Clear();
                            });
                            componentGenericMenu.ShowAsContext();
                        }
                        else
                        {
                            DisplayObjectContextMenuDelegate(rect, component, 0);
                        }

                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.MouseUp)
                {
                    // 中クリック：インスペクター表示
                    if (currentEvent.button == 2)
                    {
                        List<UnityEngine.Object> inspectorComponents = new List<UnityEngine.Object>();

                        foreach (var selectedComponent in selectedComponents)
                            inspectorComponents.Add(selectedComponent.Value);

                        if (!selectedComponents.ContainsKey(comHash))
                            inspectorComponents.Add(component);

                        var window = InstantInspector.OpenEditor();
                        window.Fill(inspectorComponents,
                            currentEvent.alt ? InstantInspector.FillMode.Add : InstantInspector.FillMode.Default);
                        window.Focus();

                        currentEvent.Use();
                        return;
                    }
                }
            }

            if (selectedComponents.Count > 0 &&
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                !currentEvent.control &&
                !rect.Contains(currentEvent.mousePosition))
            {
                selectedComponents.Clear();
                activeComponent = null;
            }
        }

        void BottomRightArea(Rect rect)
        {
            // var content = new GUIContent(string.Format("{0}", VERSION));
            // rect = RectFromRight(rect, EditorStyles.miniBoldLabel.CalcSize(content).x, 0);
            // rect.y += Screen.height - 59;
            // GUI.color = new Color(.5f, .5f, .5f, .2f);
            // GUI.Label(rect, content, EditorStyles.miniBoldLabel);
            // GUI.color = Color.white;
        }

        void Background(Rect rect)
        {
            // rect.y += 16;
            // rect.xMin = 0;
            // rect.height = Screen.height;
            // GUI.color = new Color(.4f, .4f, .4f, 1);
            // GUI.DrawTexture(rect, Assets.PixelWhite);
            // GUI.color = Color.white;
        }

        // タグの表示と変更メニュー
        void DisplayTag()
        {
            GUIContent tagContent = new GUIContent(rowItem.gameObject.tag);

            var style = Styles.Tag;
            style.normal.textColor = ThemeData.tagColor;
            Rect rect;

            if (settings.tagAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                rect = RectFromLeft(rowItem.nameRect, style.CalcSize(tagContent).x, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromLeft(rowItem.nameRect, 1, widthUse.afterName), ThemeData.colorGrid);
                    GUI.Label(rect, tagContent, style);
                }
            }
            else
            {
                rect = RectFromRight(rowItem.rect, style.CalcSize(tagContent).x, ref widthUse.right);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromRight(rowItem.rect, 1, widthUse.right), ThemeData.colorGrid);
                    GUI.Label(rect, tagContent, style);
                }
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GenericMenu menuTags = new GenericMenu();
                GameObject gameObject = rowItem.gameObject;

                menuTags.AddItem(new GUIContent("Apply All Children"), settings.applyTagTargetAndChild,
                    () => { settings.applyTagTargetAndChild = !settings.applyTagTargetAndChild; });
                menuTags.AddSeparator("");

                foreach (var tag in InternalEditorUtility.tags)
                {
                    menuTags.AddItem(new GUIContent(tag), gameObject.tag == tag ? true : false, () =>
                    {
                        if (settings.applyTagTargetAndChild)
                            ApplyTagTargetAndChild(gameObject.transform, tag);
                        else
                        {
                            Undo.RegisterCompleteObjectUndo(gameObject, "Change Tag");
                            gameObject.tag = tag;
                        }
                    });
                }

                menuTags.ShowAsContext();
                currentEvent.Use();
            }
        }

        // タグを子オブジェクトにも適用する再帰関数
        void ApplyTagTargetAndChild(Transform target, string tag)
        {
            Undo.RegisterCompleteObjectUndo(target.gameObject, "Change Tag");
            target.gameObject.tag = tag;

            for (int i = 0; i < target.childCount; ++i)
                ApplyTagTargetAndChild(target.GetChild(i), tag);
        }

        // レイヤーの表示と変更メニュー
        void DisplayLayer()
        {
            GUIContent layerContent = new GUIContent(LayerMask.LayerToName(rowItem.gameObject.layer));
            var style = Styles.Layer;
            style.normal.textColor = ThemeData.layerColor;
            Rect rect;

            if (settings.layerAlignment == HierarchySettings.ElementAlignment.AfterName)
            {
                rect = RectFromLeft(rowItem.nameRect, style.CalcSize(layerContent).x, ref widthUse.afterName);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromLeft(rowItem.nameRect, 1, widthUse.afterName), ThemeData.colorGrid);
                    GUI.Label(rect, layerContent, style);
                }
            }
            else
            {
                rect = RectFromRight(rowItem.rect, style.CalcSize(layerContent).x, ref widthUse.right);

                if (currentEvent.type == EventType.Repaint)
                {
                    GUISeparator(RectFromRight(rowItem.rect, 1, widthUse.right), ThemeData.colorGrid);
                    GUI.Label(rect, layerContent, style);
                }
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 &&
                rect.Contains(currentEvent.mousePosition))
            {
                GenericMenu menuLayers = new GenericMenu();
                GameObject gameObject = rowItem.gameObject;

                menuLayers.AddItem(new GUIContent("Apply All Children"), settings.applyLayerTargetAndChild,
                    () => { settings.applyLayerTargetAndChild = !settings.applyLayerTargetAndChild; });
                menuLayers.AddSeparator("");

                foreach (string layer in InternalEditorUtility.layers)
                {
                    menuLayers.AddItem(new GUIContent(layer),
                        LayerMask.NameToLayer(layer) == gameObject.layer ? true : false, () =>
                        {
                            if (settings.applyLayerTargetAndChild)
                                ApplyLayerTargetAndChild(gameObject.transform, LayerMask.NameToLayer(layer));
                            else
                            {
                                Undo.RegisterCompleteObjectUndo(gameObject, "Change Layer");
                                gameObject.layer = LayerMask.NameToLayer(layer);
                            }
                        });
                }

                menuLayers.ShowAsContext();
                currentEvent.Use();
            }
        }

        // レイヤーを子オブジェクトにも適用する再帰関数
        void ApplyLayerTargetAndChild(Transform target, int layer)
        {
            Undo.RegisterCompleteObjectUndo(target.gameObject, "Change Layer");
            target.gameObject.layer = layer;

            for (int i = 0; i < target.childCount; ++i)
                ApplyLayerTargetAndChild(target.GetChild(i), layer);
        }

        // 行の背景色を描画
        void DisplayRowBackground(bool nextRow = true)
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            Rect rect = rowItem.rect;
            rect.xMin = -1;
            rect.width += 16;

            Color color = (rect.y / rect.height) % 2 == 0 ? ThemeData.colorRowEven : ThemeData.colorRowOdd;

            if (nextRow)
                rect.y += rect.height;

            Color guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        // グリッド線を描画
        void DisplayGrid()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            var rect = rowItem.rect;

            rect.xMin = GLOBAL_SPACE_OFFSET_LEFT;
            rect.y += 15;
            rect.width += 16;
            rect.height = 1;

            Color guiColor = GUI.color;
            GUI.color = ThemeData.colorGrid;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        // ツリー構造（枝）の表示
        void DisplayTreeView()
        {
            if (currentEvent.type != EventType.Repaint)
                return;

            Rect rect = rowItem.rect;

            rect.width = 40;
            rect.x -= 34;
            var t = rowItem.gameObject.transform.parent;

            Color guiColor = GUI.color;
            GUI.color = ThemeData.colorTreeView;

            if (t.childCount == 1 || t.GetChild(t.childCount - 1) == rowItem.gameObject.transform)
            {
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_L"), ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_T"), ScaleMode.ScaleToFit);
            }

            while (t != null)
            {
                if (t.parent == null)
                    break;

                if (t == t.parent.GetChild(t.parent.childCount - 1))
                {
                    t = t.parent;
                    rect.x -= 14;
                    continue;
                }

                rect.x -= 14;
                GUI.DrawTexture(rect, resources.GetIcon("icon_branch_I"), ScaleMode.ScaleToFit);
                t = t.parent;
            }

            GUI.color = guiColor;
        }

        GUIContent tmpSceneContent = new GUIContent();

        // シーン名のリネームUI
        void RenameSceneInHierarchy()
        {
            string name = currentScene.name;
            if (name == "")
                return;

            var leftTitleWidthUsed = 48f;
#if UNITY_2019_1_OR_NEWER
            leftTitleWidthUsed += 24f;
#endif

            if (!currentScene.isLoaded)
                name = string.Format("{0} (not loaded", name);

            tmpSceneContent.text = name == "" ? "Untitled" : name;
            Vector2 size = Styles.TreeBoldLabel.CalcSize(tmpSceneContent);
            leftTitleWidthUsed += size.x;


            if (currentEvent.type == EventType.KeyDown &&
                currentEvent.keyCode == KeyCode.F2 &&
                rowItem.rect.Contains(currentEvent.mousePosition))
            {
                SceneRenamePopup.ShowPopup(currentScene);
            }
        }

        void CollapseAll()
        {
        }

        void DirtyScene(Scene scene)
        {
            if (EditorApplication.isPlaying)
                return;

            EditorSceneManager.MarkSceneDirty(scene);
        }

        // 最初の要素かどうかの判定
        bool IsFirstElement(Rect rect) => previousRowIndex > rect.y / rect.height;

        // 最初の行かどうかの判定
        bool IsFirstRow(Rect rect) => rect.y / rect.height == 0;

        // 行インデックスの取得
        int GetRowIndex(Rect rect) => (int) (rect.y / rect.height);

        // 選択中かどうかの判定
        bool InSelection(int ID) => Selection.Contains(ID) ? true : false;

        bool IsElementDirty(int ID) => EditorUtility.IsDirty(ID);

        // 右側基準でRectを計算するユーティリティ
        Rect RectFromRight(Rect rect, float width, float usedWidth)
        {
            usedWidth += width;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        // 右側基準でRectを計算し、使用幅を更新する
        Rect RectFromRight(Rect rect, float width, ref float usedWidth)
        {
            usedWidth += width;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        Rect RectFromRight(Rect rect, Vector2 offset, float width, ref float usedWidth)
        {
            usedWidth += width;
            rect.position += offset;
            rect.x = rect.x + rect.width - usedWidth;
            rect.width = width;
            return rect;
        }

        // 左側基準でRectを計算するユーティリティ
        Rect RectFromLeft(Rect rect, float width, float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        // 左側基準でRectを計算し、使用幅を更新する
        Rect RectFromLeft(Rect rect, float width, ref float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        Rect RectFromLeft(Rect rect, Vector2 offset, float width, ref float usedWidth, bool usexmin = true)
        {
            if (usexmin)
                rect.xMin = 0;
            rect.position += offset;
            rect.x += usedWidth;
            rect.width = width;
            usedWidth += width;
            return rect;
        }

        // GUIのセパレータ（縦線など）を描画
        void GUISeparator(Rect rect, Color color)
        {
            Color guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Resources.PixelWhite, ScaleMode.StretchToFill);
            GUI.color = guiColor;
        }

        // 幅の使用状況を保持する構造体
        struct WidthUse
        {
            public float left;
            public float right;
            public float afterName;

            public WidthUse(float left, float right, float afterName)
            {
                this.left = left;
                this.right = right;
                this.afterName = afterName;
            }

            public static WidthUse zero
            {
                get { return new WidthUse(0, 0, 0); }
            }
        }

        // 個々のHierarchyウィンドウを管理する内部クラス
        sealed class HierarchyWindow
        {
            public static Dictionary<int, EditorWindow> instances = new Dictionary<int, EditorWindow>();
            public static List<HierarchyWindow> windows = new List<HierarchyWindow>();

            public int instanceID = Int32.MinValue;
            public EditorWindow editorWindow;
            public object treeview;

            public HierarchyWindow(EditorWindow editorWindow)
            {
                this.editorWindow = editorWindow;

                instanceID = this.editorWindow.GetInstanceID();

                instances.Add(instanceID, this.editorWindow);
                windows.Add(this);

                // Debug.Log(string.Format("HierarchyWindow {0} Instanced.", instanceID));

                Reflection();
            }

            public void Reflection()
            {
                // treeview = m_TreeView.GetValue(m_SceneHierarchy.GetValue(editorWindow));
            }

            public void Dispose()
            {
                editorWindow = null;
                treeview = null;
                instances.Remove(instanceID);
                windows.Remove(this);

                // Debug.Log(string.Format("HierarchyWindow {0} Disposed.", instanceID));
            }

            public TreeViewItem GetItemAndRowIndex(int id, out int row)
            {
                row = -1;
                // if (treeview == null) return null;
                // var item = GetItemAndRowIndexMethod.Invoke(treeview, new object[] {id, row}) as TreeViewItem;
                // return item;
                return null;
            }

            public void SetWindowTitle(string value)
            {
                if (editorWindow == null)
                    return;

                editorWindow.titleContent.text = value;
            }
        }

        // 行アイテムのデータ保持クラス
        sealed class RowItem
        {
            public int ID = int.MinValue;
            public Rect rect;
            public Rect nameRect;
            public int rowIndex = 0;
            public GameObject gameObject;
            public bool isNull = true;
            public bool isPrefab = false;
            public bool isPrefabMissing = false;
            public bool isRootObject = false;
            public bool isSelected = false;
            public bool isFirstRow = false;
            public bool isFirstElement = false;
            public bool isSeparator = false;
            public bool isFolder = false;
            public bool isDirty = false;
            public bool isMouseHovering = false;
            public HierarchyFolder hierarchyFolder;

            public string name
            {
                get { return isNull ? "Null" : gameObject.name; }
            }

            public int childCount
            {
                get { return gameObject.transform.childCount; }
            }

            public Scene Scene
            {
                get { return gameObject.scene; }
            }

            public bool isStatic
            {
                get { return isNull ? false : gameObject.isStatic; }
            }

            public RowItem()
            {
            }

            public void Dispose()
            {
                ID = int.MinValue;
                gameObject = null;
                rect = Rect.zero;
                nameRect = Rect.zero;
                rowIndex = 0;
                isNull = true;
                isRootObject = false;
                isSelected = false;
                isFirstRow = false;
                isFirstElement = false;
                isSeparator = false;
                isFolder = false;
                isDirty = false;
                isMouseHovering = false;
            }
        }

        // テクスチャリソース管理クラス
        internal sealed class Resources
        {
            private static Texture2D pixelWhite;

            public static Texture2D PixelWhite
            {
                get
                {
                    if (pixelWhite == null)
                    {
                        pixelWhite = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                        pixelWhite.SetPixel(0, 0, Color.white);
                        pixelWhite.Apply();
                    }

                    return pixelWhite;
                }
            }

            private static Texture2D alphaTexture;

            public static Texture2D AlphaTexture
            {
                get
                {
                    if (alphaTexture == null)
                    {
                        alphaTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                        for (int x = 0; x < 16; ++x)
                        for (int y = 0; y < 16; ++y)
                            alphaTexture.SetPixel(x, y, Color.clear);
                        alphaTexture.Apply();
                    }

                    return alphaTexture;
                }
            }

            private static Texture2D ramp8x8White;

            public static Texture2D Ramp8x8White
            {
                get
                {
                    if (ramp8x8White == null)
                    {
                        ramp8x8White = new byte[]
                        {
                            137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 16,
                            0, 0, 0, 16, 8, 6, 0, 0, 0, 31, 243, 255, 97, 0, 0, 0, 40, 73, 68, 65, 84, 56, 17, 99, 252,
                            15, 4, 12, 12,
                            12, 31, 8, 224, 143, 184, 228, 153, 128, 18, 20, 129, 81, 3, 24, 24, 70, 195, 96, 52, 12,
                            64, 153, 104, 224,
                            211, 1, 0, 153, 171, 18, 45, 165, 62, 165, 211, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130
                        }.PNGImageDecode();
                    }

                    return ramp8x8White;
                }
            }

            internal static readonly Texture lockIconOn = EditorGUIUtility.IconContent("LockIcon-On").image;

            private static Texture folderIcon;

            public static Texture FolderIcon
            {
                get
                {
                    if (folderIcon == null)
                        folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
                    return folderIcon;
                }
            }

            private static Texture emptyFolderIcon;

            public static Texture EmptyFolderIcon
            {
                get
                {
                    if (emptyFolderIcon == null)
                        emptyFolderIcon = EditorGUIUtility.IconContent("FolderEmpty Icon").image;
                    return emptyFolderIcon;
                }
            }
        }

        // GUIスタイル管理クラス
        internal static class Styles
        {
            internal static GUIStyle lineStyle = new GUIStyle("TV Line");

            internal static GUIStyle PR_DisabledLabel = new GUIStyle("PR DisabledLabel");

            internal static GUIStyle PR_PrefabLabel = new GUIStyle("PR PrefabLabel");

            internal static GUIStyle PR_DisabledPrefabLabel = new GUIStyle("PR DisabledPrefabLabel");

            internal static GUIStyle PR_BrokenPrefabLabel = new GUIStyle("PR BrokenPrefabLabel");

            internal static GUIStyle PR_DisabledBrokenPrefabLabel = new GUIStyle("PR DisabledBrokenPrefabLabel");

            internal static GUIStyle Tag = new GUIStyle()
            {
                padding = new RectOffset(3, 4, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 8,
                richText = true,
                border = new RectOffset(12, 12, 8, 8),
            };

            internal static GUIStyle Layer = new GUIStyle()
            {
                padding = new RectOffset(3, 4, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 8,
                richText = true,
                border = new RectOffset(12, 12, 8, 8),
            };

            [System.Obsolete] internal static GUIStyle DirtyLabel = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(-1, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.UpperLeft,
            };

            internal static GUIStyle Header = new GUIStyle(TreeBoldLabel)
            {
                richText = true,
                normal = new GUIStyleState() {textColor = Color.white}
            };

            internal static GUIStyle TreeBoldLabel
            {
                get { return UnityEditor.IMGUI.Controls.TreeView.DefaultStyles.boldLabel; }
            }

            internal static GUIStyle TreeLabel = new GUIStyle(UnityEditor.IMGUI.Controls.TreeView.DefaultStyles.label)
            {
                richText = true,
                normal = new GUIStyleState() {textColor = Color.white}
            };
        }

        // メニューコマンド定義クラス
        internal sealed class MenuCommand
        {
            const int priority = 200;

            [MenuItem("Tools/Hierarchy 2/Lock Selection %l", false, priority)]
            static void SetNotEditableObject()
            {
                Undo.RegisterCompleteObjectUndo(Selection.gameObjects, "Set Selections Flag NotEditable");
                foreach (GameObject gameObject in Selection.gameObjects)
                {
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component)
                        {
                            Undo.RegisterCompleteObjectUndo(component, "Set Selections Flag NotEditable");
                            component.hideFlags = HideFlags.NotEditable;
                        }
                    }
                }

                foreach (GameObject gameObject in Selection.gameObjects)
                    gameObject.hideFlags = HideFlags.NotEditable;

                InternalEditorUtility.RepaintAllViews();
            }

            [MenuItem("Tools/Hierarchy 2/Lock Selection %l", true, priority)]
            static bool ValidateSetNotEditableObject() => Selection.gameObjects.Length > 0;

            [MenuItem("Tools/Hierarchy 2/Unlock Selection %&l", false, priority)]
            static void SetEditableObject()
            {
                Undo.RegisterCompleteObjectUndo(Selection.gameObjects, "Set Selections Flag Editable");
                foreach (GameObject gameObject in Selection.gameObjects)
                {
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component)
                        {
                            Undo.RegisterCompleteObjectUndo(component, "Set Selections Flag Editable");
                            component.hideFlags = HideFlags.None;
                        }
                    }
                }

                foreach (GameObject gameObject in Selection.gameObjects)
                    gameObject.hideFlags = HideFlags.None;

                InternalEditorUtility.RepaintAllViews();
            }

            [MenuItem("Tools/Hierarchy 2/Unlock Selection %&l", true, priority)]
            static bool ValidateSetEditableObject() => Selection.gameObjects.Length > 0;


            [MenuItem("Tools/Hierarchy 2/Move Selection Up #w", false, priority)]
            static void QuickSiblingUp()
            {
                var gameObject = Selection.activeGameObject;
                if (gameObject == null)
                    return;

                var index = gameObject.transform.GetSiblingIndex();
                if (index > 0)
                {
                    Undo.SetTransformParent(gameObject.transform, gameObject.transform.parent, string.Format("{0} Parenting", gameObject.name));

                    gameObject.transform.SetSiblingIndex(--index);
                }
            }

            [MenuItem("Tools/Hierarchy 2/Move Selection Up #w", true)]
            static bool ValidateQuickSiblingUp() => Selection.activeTransform != null;

            [MenuItem("Tools/Hierarchy 2/Move Selection Down #s", false, priority)]
            static void QuickSiblingDown()
            {
                var gameObject = Selection.activeGameObject;
                if (gameObject == null)
                    return;

                Undo.SetTransformParent(gameObject.transform, gameObject.transform.parent, string.Format("{0} Parenting", gameObject.name));

                var index = gameObject.transform.GetSiblingIndex();
                gameObject.transform.SetSiblingIndex(++index);
            }

            [MenuItem("Tools/Hierarchy 2/Move Selection Down #s", true, priority)]
            static bool ValidateQuickSiblingDown() => Selection.activeTransform != null;

            [MenuItem("Tools/Hierarchy 2/Separator", priority = 0)]
            static void CreateHeaderInstance(UnityEditor.MenuCommand command)
            {
                GameObject gameObject = new GameObject(string.Format("{0}Separator", HierarchyEditor.instance.settings.separatorStartWith));

                Undo.RegisterCreatedObjectUndo(gameObject, "Create Separator");
                // Don't create headers as children of the selected objects because only root headers are drawn with background
                //if(command.context)
                //    Undo.SetTransformParent(gameObject.transform, ( (GameObject) command.context ).transform, "Create Header");

                Selection.activeTransform = gameObject.transform;
            }

            [MenuItem("Tools/Hierarchy 2/Settings", priority = 0)]
            static void OpenHierarchyProjectSettings()
            {
                SettingsService.OpenProjectSettings("Project/Hierarchy");
            }
        }
    }
}