using UnityEngine;

namespace VLab.Bootstrap
{
    public sealed class Bootstrap : MonoBehaviour
    {
        #region Evaluation API

        /// <summary>Send evaluation score to LMS. Call from any module scene.</summary>
        public static void SendEvaluation(int score)
        {
            Debug.Log($"[Bootstrap] Evaluation: {score}");
        }

        #endregion
    }
}