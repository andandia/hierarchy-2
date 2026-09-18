#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hierarchy2
{
    /// <summary>
    /// Unity 6の新Hierarchy (UI Toolkit) に対応した拡張クラス (Hierarchy2-re)。
    /// - 特定のプレフィックスで始まるルートオブジェクトのヘッダー（セパレータ）表示
    /// - 特定の文字列で始まるオブジェクトの行背景色変更 (Instant Background)
    /// - マウス中央クリックによるアクティブ状態切り替え
    /// - Tools/Hierarchy2-re メニューによる有効/無効トグルおよびヘッダー作成
    /// </summary>
    [InitializeOnLoad]
    internal static class ModernHierarchyExtension
    {
        private const string MENU_ENABLE_PATH = "Tools/Hierarchy2-re/Enable Hierarchy2-re";
        private const string MENU_SEPARATOR_PATH = "Tools/Hierarchy2-re/Separator";
        private const string MENU_SETTINGS_PATH = "Tools/Hierarchy2-re/Settings";
        private const string HEADER_OVERLAY_NAME = "hierarchy2-header-overlay";

        private static bool isInitialized = false;

        /// <summary>
        /// 静的コンストラクタ。エディタ起動時およびドメインリロード時に初期化を遅延実行します。
        /// </summary>
        static ModernHierarchyExtension()
        {
            EditorApplication.delayCall += Initialize;
        }

        /// <summary>
        /// 初期化処理。設定を読み込み、有効であれば拡張を有効化します。
        /// </summary>
        private static void Initialize()
        {
            var settings = HierarchySettings.GetAssets();
            if (settings != null && settings.activeHierarchyRe)
            {
                Enable();
            }
        }

        /// <summary>
        /// メニュー項目: Hierarchy2-re の有効/無効を切り替えます。
        /// </summary>
        [MenuItem(MENU_ENABLE_PATH, false, 0)]
        private static void ToggleEnable()
        {
            var settings = HierarchySettings.GetAssets();
            if (settings == null)
            {
                return;
            }

            Undo.RecordObject(settings, "Toggle Hierarchy2-re");
            settings.activeHierarchyRe = !settings.activeHierarchyRe;
            EditorUtility.SetDirty(settings);

            if (settings.activeHierarchyRe)
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }

        /// <summary>
        /// メニューバリデーション: メニューのチェック状態を更新します。
        /// </summary>
        [MenuItem(MENU_ENABLE_PATH, true, 0)]
        private static bool ValidateToggleEnable()
        {
            var settings = HierarchySettings.GetAssets();
            bool isEnabled = settings != null && settings.activeHierarchyRe;
            Menu.SetChecked(MENU_ENABLE_PATH, isEnabled);
            return true;
        }

        /// <summary>
        /// メニュー項目: 新しいヘッダー（セパレータ）オブジェクトをルート階層に生成します。
        /// </summary>
        [MenuItem(MENU_SEPARATOR_PATH, false, 1)]
        private static void CreateHeaderInstance()
        {
            var settings = HierarchySettings.GetAssets();
            var prefix = settings != null ? settings.separatorStartWith : "--->";
            var gameObject = new GameObject(string.Format("{0}Header", prefix));

            if (settings != null && !string.IsNullOrEmpty(settings.separatorDefaultTag))
            {
                gameObject.tag = settings.separatorDefaultTag;
            }

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Header");
            Selection.activeTransform = gameObject.transform;
        }

        /// <summary>
        /// メニュー項目: Hierarchy設定ウィンドウを開きます。
        /// </summary>
        [MenuItem(MENU_SETTINGS_PATH, false, 2)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Hierarchy");
        }

        /// <summary>
        /// 拡張機能を有効化し、新Hierarchyイベントおよびマウスコールバックを登録します。
        /// </summary>
        public static void Enable()
        {
            if (isInitialized)
            {
                return;
            }

            isInitialized = true;
            HierarchyWindow.BindView += OnBindView;
            HierarchyWindow.UnbindView += OnUnbindView;
            HierarchyWindow.BindViewItem += OnBindViewItem;
            HierarchyWindow.UnbindViewItem += OnUnbindViewItem;

            var settings = HierarchySettings.GetAssets();
            if (settings != null)
            {
                settings.onSettingsChanged -= OnSettingsChanged;
                settings.onSettingsChanged += OnSettingsChanged;
            }

            // 開いている全HierarchyWindowのViewにコールバックを登録し、スタイルを反映
            var windows = Resources.FindObjectsOfTypeAll<HierarchyWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].View != null)
                {
                    OnBindView(windows[i], windows[i].View);
                }
            }

            RefreshAllItems();
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// 拡張機能を無効化し、イベントを解除してスタイルを元に戻します。
        /// </summary>
        public static void Disable()
        {
            if (!isInitialized)
            {
                return;
            }

            isInitialized = false;
            HierarchyWindow.BindView -= OnBindView;
            HierarchyWindow.UnbindView -= OnUnbindView;
            HierarchyWindow.BindViewItem -= OnBindViewItem;
            HierarchyWindow.UnbindViewItem -= OnUnbindViewItem;

            var settings = HierarchySettings.GetAssets();
            if (settings != null)
            {
                settings.onSettingsChanged -= OnSettingsChanged;
            }

            // 開いている全HierarchyWindowのViewからコールバックを解除しスタイルをクリア
            var windows = Resources.FindObjectsOfTypeAll<HierarchyWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].View != null)
                {
                    OnUnbindView(windows[i], windows[i].View);
                    var items = windows[i].View.Query<HierarchyViewItem>().ToList();
                    for (int j = 0; j < items.Count; j++)
                    {
                        ResetItemStyle(items[j]);
                    }
                }
            }

            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// 現在開いているすべてのHierarchyWindowのアイテムに対してスタイルを再適用します。
        /// </summary>
        public static void RefreshAllItems()
        {
            var windows = Resources.FindObjectsOfTypeAll<HierarchyWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].View != null)
                {
                    var items = windows[i].View.Query<HierarchyViewItem>().ToList();
                    for (int j = 0; j < items.Count; j++)
                    {
                        ApplyItemStyle(items[j]);
                    }
                }
            }
        }

        /// <summary>
        /// 設定が変更された際に呼び出され、全アイテムのスタイル更新とウィンドウ再描画を要求します。
        /// </summary>
        private static void OnSettingsChanged(string param)
        {
            RefreshAllItems();
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// HierarchyViewがバインドされた際に、マウスイベントハンドラを登録します。
        /// </summary>
        private static void OnBindView(HierarchyWindow window, HierarchyView view)
        {
            view.UnregisterCallback<PointerUpEvent>(OnViewPointerUp, TrickleDown.TrickleDown);
            view.RegisterCallback<PointerUpEvent>(OnViewPointerUp, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// HierarchyViewがアンバインドされた際に、マウスイベントハンドラを解除します。
        /// </summary>
        private static void OnUnbindView(HierarchyWindow window, HierarchyView view)
        {
            view.UnregisterCallback<PointerUpEvent>(OnViewPointerUp, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 各アイテムが表示・バインドされる際に、設定に基づいてスタイルを適用します。
        /// </summary>
        private static void OnBindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            ApplyItemStyle(item);
        }

        /// <summary>
        /// 指定されたHierarchyViewItemに設定に基づいたスタイル（ヘッダーまたは背景色）を適用します。
        /// </summary>
        public static void ApplyItemStyle(HierarchyViewItem item)
        {
            if (item == null || item.RowContainer == null)
            {
                return;
            }

            if (item.Handler is not HierarchyGameObjectHandler gameObjectHandler)
            {
                ResetItemStyle(item);
                return;
            }

            var node = item.Node;
            var gameObject = gameObjectHandler.GetGameObject(node);
            if (gameObject == null)
            {
                ResetItemStyle(item);
                return;
            }

            var settings = HierarchySettings.GetAssets();
            if (settings == null || !settings.activeHierarchyRe)
            {
                ResetItemStyle(item);
                return;
            }

            // 1. ヘッダー（セパレータ）の判定と適用
            bool isRoot = gameObject.transform.parent == null;
            bool isSeparator = isRoot &&
                               !string.IsNullOrEmpty(settings.separatorStartWith) &&
                               gameObject.name.StartsWith(settings.separatorStartWith);

            if (isSeparator)
            {
                // セパレータタグの自動補正
                if (!string.IsNullOrEmpty(settings.separatorDefaultTag) && !gameObject.CompareTag(settings.separatorDefaultTag))
                {
                    gameObject.tag = settings.separatorDefaultTag;
                }

                var theme = settings.usedTheme;
                item.RowContainer.style.backgroundColor = theme.colorHeaderBackground;

                // 通常のヒエラルキーアイコン・トグル・ラベルを非表示
                if (item.Icon != null)
                {
                    item.Icon.style.visibility = Visibility.Hidden;
                }

                if (item.Toggle != null)
                {
                    item.Toggle.style.visibility = Visibility.Hidden;
                }

                if (item.Name != null)
                {
                    item.Name.style.visibility = Visibility.Hidden;
                }

                // ヘッダー用の中央揃えオーバーレイラベルを取得または作成
                var overlay = item.RowContainer.Q<Label>(HEADER_OVERLAY_NAME);
                if (overlay == null)
                {
                    overlay = new Label();
                    overlay.name = HEADER_OVERLAY_NAME;
                    overlay.pickingMode = PickingMode.Ignore;

                    overlay.style.position = Position.Absolute;
                    overlay.style.left = 0f;
                    overlay.style.right = 0f;
                    overlay.style.top = 0f;
                    overlay.style.bottom = 0f;
                    overlay.style.unityTextAlign = TextAnchor.MiddleCenter;
                    overlay.style.unityFontStyleAndWeight = FontStyle.Bold;
                    overlay.style.fontSize = 12f;

                    item.RowContainer.Add(overlay);
                }

                overlay.style.display = DisplayStyle.Flex;
                var title = gameObject.name.Substring(settings.separatorStartWith.Length).Trim();
                overlay.text = title;
                overlay.style.color = theme.colorHeaderTitle;
                return;
            }

            // 2. セパレータではない場合、ヘッダー表示をリセット
            var existingOverlay = item.RowContainer.Q<Label>(HEADER_OVERLAY_NAME);
            if (existingOverlay != null)
            {
                existingOverlay.style.display = DisplayStyle.None;
            }

            if (item.Icon != null)
            {
                item.Icon.style.visibility = StyleKeyword.Null;
            }

            if (item.Toggle != null)
            {
                item.Toggle.style.visibility = StyleKeyword.Null;
            }

            if (item.Name != null)
            {
                item.Name.style.visibility = StyleKeyword.Null;
            }

            // 3. Instant Background の判定と適用
            if (!settings.useInstantBackground)
            {
                item.RowContainer.style.backgroundColor = StyleKeyword.Null;
                return;
            }

            bool matched = false;
            Color matchedColor = default;

            for (int i = 0; i < settings.instantBackgroundColors.Count; ++i)
            {
                var bgConfig = settings.instantBackgroundColors[i];
                if (!bgConfig.active)
                {
                    continue;
                }

                if ((bgConfig.useStartWith && !string.IsNullOrEmpty(bgConfig.startWith) && gameObject.name.StartsWith(bgConfig.startWith)) ||
                    (bgConfig.useTag && !string.IsNullOrEmpty(bgConfig.tag) && gameObject.CompareTag(bgConfig.tag)) ||
                    (bgConfig.useLayer && (1 << gameObject.layer & bgConfig.layer) != 0))
                {
                    matched = true;
                    matchedColor = bgConfig.color;
                }
            }

            if (matched)
            {
                item.RowContainer.style.backgroundColor = matchedColor;
            }
            else
            {
                item.RowContainer.style.backgroundColor = StyleKeyword.Null;
            }
        }

        /// <summary>
        /// アイテムのスタイル（背景色やヘッダー表示）を通常状態にリセットします。
        /// </summary>
        public static void ResetItemStyle(HierarchyViewItem item)
        {
            if (item == null || item.RowContainer == null)
            {
                return;
            }

            item.RowContainer.style.backgroundColor = StyleKeyword.Null;

            var overlay = item.RowContainer.Q<Label>(HEADER_OVERLAY_NAME);
            if (overlay != null)
            {
                overlay.style.display = DisplayStyle.None;
            }

            if (item.Icon != null)
            {
                item.Icon.style.visibility = StyleKeyword.Null;
            }

            if (item.Toggle != null)
            {
                item.Toggle.style.visibility = StyleKeyword.Null;
            }

            if (item.Name != null)
            {
                item.Name.style.visibility = StyleKeyword.Null;
            }
        }

        /// <summary>
        /// アイテムがアンバインドされた際に、スタイルをリセットします。
        /// </summary>
        private static void OnUnbindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            ResetItemStyle(item);
        }

        /// <summary>
        /// マウスイベントハンドラ。中央クリック（ホイールクリック）時に対象GameObjectのアクティブ状態を反転します。
        /// </summary>
        private static void OnViewPointerUp(PointerUpEvent evt)
        {
            if (evt.button != (int)MouseButton.MiddleMouse)
            {
                return;
            }

            var target = evt.target as VisualElement;
            var viewItem = target?.GetFirstAncestorOfType<HierarchyViewItem>() ?? target as HierarchyViewItem;
            if (viewItem == null)
            {
                return;
            }

            if (viewItem.Handler is not HierarchyGameObjectHandler gameObjectHandler)
            {
                return;
            }

            var node = viewItem.Node;
            var gameObject = gameObjectHandler.GetGameObject(node);
            if (gameObject == null)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(
                gameObject,
                gameObject.activeSelf ? "Inactive object" : "Active object"
            );
            gameObject.SetActive(!gameObject.activeSelf);

            evt.StopImmediatePropagation();
        }
    }
}
#endif
