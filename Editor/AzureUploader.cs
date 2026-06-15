using System;
using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class AzureUploader
    {
        public const string PrefContainerSasUrl = "BuildTools.Azure.ContainerSasUrl";

        public static bool IsConfigured =>
            !string.IsNullOrEmpty(EditorPrefs.GetString(PrefContainerSasUrl, ""));

        public static bool Upload(string filePath, string blobName)
        {
            string containerSasUrl = EditorPrefs.GetString(PrefContainerSasUrl, "");
            if (string.IsNullOrEmpty(containerSasUrl))
            {
                Debug.LogWarning("BuildTools: Azure SAS URL not configured — skipping upload.");
                return false;
            }

            var uri = new Uri(containerSasUrl);
            string blobUrl = $"{uri.GetLeftPart(UriPartial.Path)}/{blobName}{uri.Query}";

            Debug.Log($"BuildTools: uploading {blobName} → Azure...");

            try
            {
                var req = (HttpWebRequest)WebRequest.Create(blobUrl);
                req.Method = "PUT";
                req.Headers["x-ms-blob-type"] = "BlockBlob";
                req.ContentType = "application/octet-stream";

                var fileInfo = new FileInfo(filePath);
                req.ContentLength = fileInfo.Length;

                using (var fs = File.OpenRead(filePath))
                using (var rs = req.GetRequestStream())
                    fs.CopyTo(rs);

                using var resp = (HttpWebResponse)req.GetResponse();
                bool ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
                if (ok)
                {
                    var uri2 = new Uri(containerSasUrl);
                    string downloadUrl = $"{uri2.GetLeftPart(UriPartial.Path)}/{blobName}{uri2.Query}";
                    Debug.Log($"BuildTools: upload succeeded — {fileInfo.Length / 1024 / 1024} MB\nDownload: {downloadUrl}");
                }
                else
                    Debug.LogError($"BuildTools: upload failed — HTTP {(int)resp.StatusCode}");
                return ok;
            }
            catch (WebException ex)
            {
                string body = ex.Response != null
                    ? new StreamReader(ex.Response.GetResponseStream()).ReadToEnd()
                    : ex.Message;
                Debug.LogError($"BuildTools: upload failed — {body}");
                return false;
            }
        }
    }
}
