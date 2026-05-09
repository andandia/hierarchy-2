using UnityEditor;
using UnityEngine;

namespace Hierarchy2
{
    /// <summary>
    /// ゲームの速度(Time.timeScale)を変更するエディタウィンドウ
    /// </summary>
    [InitializeOnLoad]
    public class GameSpeedWindow : EditorWindow
    {
        // クラス初期化時にプレイモードの変更イベントを購読
        static GameSpeedWindow()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Hierarchy2/Game Speed")]
        public static void ShowWindow()
        {
            var window = GetWindow<GameSpeedWindow>("Game Speed");
            window.Show();
        }

        // 対象のAudioSourceを保持
        private AudioSource targetAudioSource;

        private void OnGUI()
        {
            GUILayout.Label("ゲーム速度設定", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();

            // スライダーの表示 (0.1 から 2.0 まで)
            float newTimeScale = EditorGUILayout.Slider("Time Scale", Time.timeScale, 0.1f, 2.0f);

            // 0.1刻みに丸める
            newTimeScale = Mathf.Round(newTimeScale * 10f) / 10f;

            if (newTimeScale != Time.timeScale)
            {
                Time.timeScale = newTimeScale;
            }

            // リセットボタン
            if (GUILayout.Button("1.0にリセット", GUILayout.Width(100)))
            {
                Time.timeScale = 1.0f;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.Label("オーディオピッチ設定", EditorStyles.boldLabel);

            // AudioSourceをアタッチするフィールド
            targetAudioSource = (AudioSource)EditorGUILayout.ObjectField("Audio Source", targetAudioSource, typeof(AudioSource), true);

            if (targetAudioSource != null)
            {
                GUILayout.BeginHorizontal();

                // ピッチスライダーの表示 (0.1 から 2.0 まで)
                float newPitch = EditorGUILayout.Slider("Pitch", targetAudioSource.pitch, 0.1f, 2.0f);

                // 0.1刻みに丸める
                newPitch = Mathf.Round(newPitch * 10f) / 10f;

                if (newPitch != targetAudioSource.pitch)
                {
                    targetAudioSource.pitch = newPitch;
                }

                // リセットボタン
                if (GUILayout.Button("1.0にリセット", GUILayout.Width(100)))
                {
                    targetAudioSource.pitch = 1.0f;
                }

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// プレイモードのステータスが変更されたときに呼ばれる
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // プレイモードに入ったとき
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // timeScaleが1.0以外の場合、ログを出力
                if (!Mathf.Approximately(Time.timeScale, 1.0f))
                {
                    Debug.Log($"[Game Speed] 現在のゲーム速度 (Time.timeScale) は {Time.timeScale} に設定されています。");
                }
            }
        }
    }
}
