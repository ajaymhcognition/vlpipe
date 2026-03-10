// Packages/com.mhcockpit.vlpipe/Editor/VLabSettings.cs
// Menu: Tools → Virtual Lab → AWS Settings

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace MHCockpit.VLPipe.Editor
{
    /// <summary>
    /// AWS Credentials Settings window.
    ///
    /// Behaviour:
    ///   — If credentials are NOT saved : shows the entry form (Senior fills this).
    ///   — If credentials ARE saved     : shows a locked status screen only.
    ///                                    No fields. No values. Junior cannot see anything.
    ///   — "Update Credentials" button  : requires confirmation before unlocking the form.
    ///                                    Senior enters new values and saves.
    ///
    /// Credentials are stored in EditorPrefs — local to this machine only.
    /// They are never written to any project file, asset, or Git repository.
    /// </summary>
    public class VLabSettings : EditorWindow
    {
        // ─────────────────────────────────────────────────────────────────────
        //  EditorPrefs storage keys  (these are key names, NOT credential values)
        // ─────────────────────────────────────────────────────────────────────
        private const string PREF_KEY_ID     = "VLab_AWS_AccessKeyId";
        private const string PREF_SECRET_KEY = "VLab_AWS_SecretKey";
        private const string PREF_REGION     = "VLab_AWS_Region";
        private const string DEFAULT_REGION  = "ap-south-1";

        // ─────────────────────────────────────────────────────────────────────
        //  Window state
        // ─────────────────────────────────────────────────────────────────────
        private string _keyId      = "";
        private string _secretKey  = "";
        private string _region     = DEFAULT_REGION;
        private bool   _showSecret = false;

        // When true the entry form is visible (new entry or senior update mode).
        private bool   _formUnlocked = false;

        private string _statusMsg  = "";
        private bool   _statusIsOk = true;

        // Colours
        private static readonly Color COL_GREEN  = new Color(0.20f, 0.75f, 0.40f);
        private static readonly Color COL_GREY   = new Color(0.55f, 0.55f, 0.55f);

        // ─────────────────────────────────────────────────────────────────────
        //  Menu
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/Virtual Lab/AWS Settings", false, 0)]
        public static void Open()
        {
            var win = GetWindow<VLabSettings>(true, "VLab — AWS Settings", true);
            win.minSize = new Vector2(440, 240);
            win.Show();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            // Always start locked — even if the form was open before.
            _formUnlocked = false;
            _statusMsg    = "";

            // Pre-fill region for when the form is eventually shown.
            _region = EditorPrefs.GetString(PREF_REGION, DEFAULT_REGION);

            // Never pre-fill key / secret into the fields.
            _keyId     = "";
            _secretKey = "";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GUI
        // ─────────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            GUILayout.Space(16);
            DrawHeader();
            GUILayout.Space(12);

            if (HasCredentials() && !_formUnlocked)
                DrawLockedView();
            else
                DrawForm();
        }

        // ── Header ────────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.Label("AWS Credentials", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();
            EditorGUILayout.HelpBox(
                "Credentials are saved on this machine only and are never " +
                "stored in the project folder or committed to Git.",
                MessageType.Info);
            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
        }

        // ── Locked view — shown to juniors when credentials already exist ─────
        private void DrawLockedView()
        {
            GUILayout.Space(12);

            // Status line
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("✔  Credentials are saved on this system.",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize  = 13,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = COL_GREEN }
                });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Region (not sensitive — fine to display)
            string region = EditorPrefs.GetString(PREF_REGION, DEFAULT_REGION);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Region : {region}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_GREY } });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(22);

            // Update button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = new Color(0.9f, 0.55f, 0.1f);
            if (GUILayout.Button("Update Credentials", GUILayout.Width(160), GUILayout.Height(28)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Update AWS Credentials",
                    "This will let you replace the saved credentials.\n\n" +
                    "Only a senior developer should do this.\n\n" +
                    "Do you want to continue?",
                    "Yes, Update",
                    "Cancel");

                if (confirmed)
                {
                    _keyId        = "";
                    _secretKey    = "";
                    _showSecret   = false;
                    _formUnlocked = true;
                    _statusMsg    = "";
                    minSize       = new Vector2(440, 300);
                    Repaint();
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Status message shown after a successful save
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Space(14);
                GUILayout.BeginVertical();
                EditorGUILayout.HelpBox(_statusMsg,
                    _statusIsOk ? MessageType.Info : MessageType.Error);
                GUILayout.EndVertical();
                GUILayout.Space(14);
                GUILayout.EndHorizontal();
            }
        }

        // ── Entry form — shown when no credentials saved, or after Update ─────
        private void DrawForm()
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 145;

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            _keyId = EditorGUILayout.TextField("Access Key ID", _keyId);

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            _secretKey = _showSecret
                ? EditorGUILayout.TextField("Secret Access Key", _secretKey)
                : EditorGUILayout.PasswordField("Secret Access Key", _secretKey);
            if (GUILayout.Button(_showSecret ? "Hide" : "Show",
                    GUILayout.Width(48), GUILayout.Height(18)))
                _showSecret = !_showSecret;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            _region = EditorGUILayout.TextField("Region", _region);

            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();

            EditorGUIUtility.labelWidth = prevLabelWidth;

            GUILayout.Space(16);

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            // Save button
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.35f);
            if (GUILayout.Button("Save Credentials", GUILayout.Height(30)))
            {
                if (string.IsNullOrWhiteSpace(_keyId) || string.IsNullOrWhiteSpace(_secretKey))
                {
                    _statusMsg  = "✗  Access Key ID and Secret Access Key are both required.";
                    _statusIsOk = false;
                }
                else
                {
                    EditorPrefs.SetString(PREF_KEY_ID,     _keyId.Trim());
                    EditorPrefs.SetString(PREF_SECRET_KEY, _secretKey.Trim());
                    EditorPrefs.SetString(PREF_REGION,
                        string.IsNullOrWhiteSpace(_region) ? DEFAULT_REGION : _region.Trim());

                    // Wipe fields from memory immediately, lock the form
                    _keyId        = "";
                    _secretKey    = "";
                    _formUnlocked = false;
                    _statusMsg    = "✓  Credentials saved successfully.";
                    _statusIsOk   = true;
                    minSize       = new Vector2(440, 240);
                    Repaint();
                }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);

            // Cancel — only shown when updating existing credentials
            if (_formUnlocked && HasCredentials())
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(30), GUILayout.Width(80)))
                {
                    _keyId        = "";
                    _secretKey    = "";
                    _formUnlocked = false;
                    _statusMsg    = "";
                    minSize       = new Vector2(440, 240);
                    Repaint();
                }
            }

            GUILayout.Space(14);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Space(14);
                GUILayout.BeginVertical();
                EditorGUILayout.HelpBox(_statusMsg,
                    _statusIsOk ? MessageType.Info : MessageType.Error);
                GUILayout.EndVertical();
                GUILayout.Space(14);
                GUILayout.EndHorizontal();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API — used by VLabBuildAndUpload
        // ─────────────────────────────────────────────────────────────────────

        public static string GetAccessKeyId() =>
            EditorPrefs.GetString(PREF_KEY_ID, "");

        public static string GetSecretKey() =>
            EditorPrefs.GetString(PREF_SECRET_KEY, "");

        public static string GetRegion()
        {
            string r = EditorPrefs.GetString(PREF_REGION, DEFAULT_REGION);
            return string.IsNullOrWhiteSpace(r) ? DEFAULT_REGION : r;
        }

        public static bool HasCredentials() =>
            !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PREF_KEY_ID,     "")) &&
            !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PREF_SECRET_KEY, ""));
    }
}

#endif // UNITY_EDITOR