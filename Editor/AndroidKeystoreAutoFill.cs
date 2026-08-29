using UnityEngine;
using UnityEditor;
using System.Security.Cryptography;
using System.Text;
using System;
using System.IO;

namespace Hierarchy2
{
    /// <summary>
    /// Androidのキーストアパスワードを暗号化して保存し、自動入力するクラス
    /// </summary>
    [InitializeOnLoad]
    public class AndroidKeystoreAutoFill
    {
        // プロジェクトごとのパスワードを分離するためのID
        // String.GetHashCode()は.NET Coreで実行毎に変わる可能性があるため、MD5ハッシュ化する
        private static readonly string ProjectId = GetMD5Hash(Application.dataPath);
        private static readonly string KeystorePassPrefKey = $"AndroidKeystoreAutoFill_KeystorePass_{ProjectId}";
        private static readonly string KeyaliasPassPrefKey = $"AndroidKeystoreAutoFill_KeyaliasPass_{ProjectId}";

        static AndroidKeystoreAutoFill()
        {
            // エディタ起動時およびコンパイル完了時に自動入力を実行する
            EditorApplication.delayCall += AutoFillPasswords;
        }

        private static string GetMD5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        private static void AutoFillPasswords()
        {
            if (EditorPrefs.HasKey(KeystorePassPrefKey))
            {
                string encryptedKeystorePass = EditorPrefs.GetString(KeystorePassPrefKey);
                string keystorePass = Decrypt(encryptedKeystorePass);
                if (!string.IsNullOrEmpty(keystorePass))
                {
                    PlayerSettings.Android.keystorePass = keystorePass;
                }
            }

            if (EditorPrefs.HasKey(KeyaliasPassPrefKey))
            {
                string encryptedKeyaliasPass = EditorPrefs.GetString(KeyaliasPassPrefKey);
                string keyaliasPass = Decrypt(encryptedKeyaliasPass);
                if (!string.IsNullOrEmpty(keyaliasPass))
                {
                    PlayerSettings.Android.keyaliasPass = keyaliasPass;
                }
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            // GUIループ内で毎回復号化を行わないように、パスワードをキャッシュする
            string cachedKeystorePass = null;
            string cachedKeyaliasPass = null;

            var provider = new SettingsProvider("Project/Android Keystore Auto-Fill", SettingsScope.Project)
            {
                label = "Android Keystore Auto-Fill",
                activateHandler = (searchContext, rootElement) =>
                {
                    // Settingsが開かれた時に一度だけ復号してキャッシュする
                    cachedKeystorePass = GetDecryptedPassword(KeystorePassPrefKey);
                    cachedKeyaliasPass = GetDecryptedPassword(KeyaliasPassPrefKey);
                },
                guiHandler = (searchContext) =>
                {
                    EditorGUILayout.LabelField("パスワードの自動入力設定", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("パスワードは暗号化され、EditorPrefsにローカル保存されます。リポジトリにはコミットされません。このPCおよびプロジェクト固有の設定です。", MessageType.Info);

                    EditorGUI.BeginChangeCheck();

                    cachedKeystorePass = EditorGUILayout.PasswordField("Keystore Password", cachedKeystorePass);
                    cachedKeyaliasPass = EditorGUILayout.PasswordField("Key Password", cachedKeyaliasPass);

                    if (EditorGUI.EndChangeCheck())
                    {
                        SaveEncryptedPassword(KeystorePassPrefKey, cachedKeystorePass);
                        SaveEncryptedPassword(KeyaliasPassPrefKey, cachedKeyaliasPass);

                        // パスワードが更新されたら直ちに反映する
                        AutoFillPasswords();
                    }

                    EditorGUILayout.Space();

                    if (GUILayout.Button("パスワードをクリア"))
                    {
                        EditorPrefs.DeleteKey(KeystorePassPrefKey);
                        EditorPrefs.DeleteKey(KeyaliasPassPrefKey);
                        PlayerSettings.Android.keystorePass = "";
                        PlayerSettings.Android.keyaliasPass = "";
                        cachedKeystorePass = "";
                        cachedKeyaliasPass = "";
                        Debug.Log("Android Keystore Auto-Fill: パスワードをクリアしました。");
                    }
                },
                keywords = new System.Collections.Generic.HashSet<string>(new[] { "Android", "Keystore", "Password", "Auto-Fill", "Security" })
            };

            return provider;
        }

        private static string GetDecryptedPassword(string prefKey)
        {
            if (EditorPrefs.HasKey(prefKey))
            {
                return Decrypt(EditorPrefs.GetString(prefKey));
            }
            return "";
        }

        private static void SaveEncryptedPassword(string prefKey, string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                EditorPrefs.DeleteKey(prefKey);
            }
            else
            {
                EditorPrefs.SetString(prefKey, Encrypt(password));
            }
        }

        // PC固有の情報を使用して暗号化キーを生成する（ハードコードを避けるため）
        private static byte[] GetEncryptionKey()
        {
            string machineInfo = SystemInfo.deviceUniqueIdentifier + Environment.MachineName + Environment.UserName;
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
            }
        }

        private static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            byte[] array;

            using (Aes aes = Aes.Create())
            {
                aes.Key = GetEncryptionKey();
                aes.GenerateIV(); // ランダムな初期化ベクトル(IV)を生成する
                byte[] iv = aes.IV;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (var memoryStream = new MemoryStream())
                {
                    // 復号化時に使用するため、IVを先頭に書き込む
                    memoryStream.Write(iv, 0, iv.Length);

                    using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        using (var streamWriter = new StreamWriter(cryptoStream))
                        {
                            streamWriter.Write(plainText);
                        }
                        array = memoryStream.ToArray();
                    }
                }
            }

            return Convert.ToBase64String(array);
        }

        private static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";

            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherText);

                if (fullCipher.Length <= 16) return "";

                byte[] iv = new byte[16];
                byte[] cipher = new byte[fullCipher.Length - 16];

                // 先頭からIVを読み取り、残りを暗号文として分割する
                Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullCipher, 16, cipher, 0, cipher.Length);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = GetEncryptionKey();
                    aes.IV = iv;

                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    using (var memoryStream = new MemoryStream(cipher))
                    {
                        using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                        {
                            using (var streamReader = new StreamReader(cryptoStream))
                            {
                                return streamReader.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 環境が変わるなどして復号化に失敗した場合は空文字を返す
                return "";
            }
        }
    }
}
