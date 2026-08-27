using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DataSakura.JitterPhysics.Editor.Export
{
    /// <summary>Editor-only preferences for sending a baked artifact to a development server.</summary>
    internal static class JitterPhysicsServerPreferences
    {
        private static string Prefix => "DataSakura.JitterPhysics." + Application.dataPath + ".";

        internal static string BaseUrl
        {
            get => EditorPrefs.GetString(Prefix + "ServerUrl", "http://127.0.0.1:5000");
            set => EditorPrefs.SetString(Prefix + "ServerUrl", value ?? string.Empty);
        }

        internal static int TimeoutSeconds
        {
            get => EditorPrefs.GetInt(Prefix + "ServerTimeout", 10);
            set => EditorPrefs.SetInt(Prefix + "ServerTimeout", Mathf.Clamp(value, 1, 120));
        }

        internal static string Token
        {
            get => EditorPrefs.GetString(Prefix + "ServerToken", string.Empty);
            set => EditorPrefs.SetString(Prefix + "ServerToken", value ?? string.Empty);
        }
    }

    /// <summary>Result of one explicit server upload.</summary>
    public sealed class JitterPhysicsServerUploadResult
    {
        internal JitterPhysicsServerUploadResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        /// <summary>True when the server accepted and stored the files.</summary>
        public bool Succeeded { get; }

        /// <summary>Server or transport explanation.</summary>
        public string Message { get; }
    }

    /// <summary>Sends verified baked bytes to the package's HTTP artifact endpoint.</summary>
    public static class JitterPhysicsServerUploader
    {
        [Serializable]
        private sealed class RequestBody
        {
            public string manifestJson;
            public string dataBase64;
        }

        [Serializable]
        private sealed class ResponseBody
        {
            public bool success;
            public bool restartRequired;
            public string levelId;
            public string artifactHash;
            public string message;
        }

        /// <summary>Starts a non-blocking upload. The callback is invoked on the editor thread.</summary>
        public static void Upload(
            JitterPhysicsArtifactDelivery delivery,
            string baseUrl,
            int timeoutSeconds,
            string token,
            Action<JitterPhysicsServerUploadResult> completed)
        {
            if (delivery == null || !delivery.Succeeded)
            {
                completed?.Invoke(new JitterPhysicsServerUploadResult(false, "No verified artifact is available."));
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri server)
                || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
            {
                completed?.Invoke(new JitterPhysicsServerUploadResult(false, "Server URL must be an absolute HTTP(S) URL."));
                return;
            }

            string endpoint = baseUrl.TrimEnd('/') + "/api/artifacts";
            string json = JsonUtility.ToJson(new RequestBody
            {
                manifestJson = delivery.ManifestJson,
                dataBase64 = Convert.ToBase64String(delivery.Payload),
            });

            var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Clamp(timeoutSeconds, 1, 120),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token))
            {
                request.SetRequestHeader("X-Jitter-Physics-Token", token);
            }

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            void Poll()
            {
                if (!operation.isDone) return;
                EditorApplication.update -= Poll;

                try
                {
                    string responseText = request.downloadHandler?.text ?? string.Empty;
                    ResponseBody response = string.IsNullOrEmpty(responseText)
                        ? null
                        : JsonUtility.FromJson<ResponseBody>(responseText);
                    bool succeeded = request.result == UnityWebRequest.Result.Success
                                     && response != null
                                     && response.success;
                    string message = response?.message;
                    if (string.IsNullOrEmpty(message))
                    {
                        message = succeeded
                            ? "Artifact uploaded. Restart the server to load it."
                            : $"Upload failed ({request.responseCode}): {request.error}";
                    }

                    completed?.Invoke(new JitterPhysicsServerUploadResult(succeeded, message));
                }
                catch (Exception exception)
                {
                    completed?.Invoke(new JitterPhysicsServerUploadResult(false, "Upload response failed: " + exception.Message));
                }
                finally
                {
                    request.Dispose();
                }
            }

            EditorApplication.update += Poll;
        }
    }
}
