using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

namespace Gosuman.BuildTools
{
    // Uploads build artifacts to a Scaleway Object Storage bucket (S3-compatible) via a
    // SigV4-signed PUT. Scaleway's `scw` CLI can create buckets but has no object upload
    // command, and no S3 client (aws/rclone/mc) is assumed to be installed on build
    // machines, so the signing is done directly here rather than shelling out — the same
    // self-contained approach AzureUploader took with its SAS URL.
    //
    // Uploaded objects get the `public-read` canned ACL (same convention the server uses
    // for chunk PNGs, see Infra/scaleway-setup.sh), so the bucket itself needs no
    // bucket-level public policy — only objects explicitly PUT with this uploader are
    // world-readable.
    public static class ScalewayUploader
    {
        public const string PrefEndpoint = "BuildTools.Scaleway.Endpoint";
        public const string PrefBucket = "BuildTools.Scaleway.Bucket";
        public const string PrefRegion = "BuildTools.Scaleway.Region";
        public const string PrefAccessKey = "BuildTools.Scaleway.AccessKey";
        public const string PrefSecretKey = "BuildTools.Scaleway.SecretKey";

        const string Service = "s3";

        public static bool IsConfigured =>
            !string.IsNullOrEmpty(EditorPrefs.GetString(PrefBucket, ""))
            && !string.IsNullOrEmpty(EditorPrefs.GetString(PrefAccessKey, ""))
            && !string.IsNullOrEmpty(EditorPrefs.GetString(PrefSecretKey, ""));

        public static bool Upload(string filePath, string objectKey)
        {
            string endpoint = EditorPrefs.GetString(PrefEndpoint, "s3.fr-par.scw.cloud");
            string bucket = EditorPrefs.GetString(PrefBucket, "");
            string region = EditorPrefs.GetString(PrefRegion, "fr-par");
            string accessKey = EditorPrefs.GetString(PrefAccessKey, "");
            string secretKey = EditorPrefs.GetString(PrefSecretKey, "");

            if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            {
                UnityEngine.Debug.LogWarning("BuildTools: Scaleway Object Storage not configured — skipping upload.");
                return false;
            }

            string host = $"{bucket}.{endpoint}";
            string canonicalUri = "/" + Uri.EscapeDataString(objectKey).Replace("%2F", "/");
            string url = $"https://{host}{canonicalUri}";

            var fileInfo = new FileInfo(filePath);
            DateTime now = DateTime.UtcNow;
            string amzDate = now.ToString("yyyyMMddTHHmmssZ");
            string dateStamp = now.ToString("yyyyMMdd");
            const string payloadHash = "UNSIGNED-PAYLOAD";

            // Headers included in the signature — sorted, lowercase names.
            var signedHeaderNames = new[] { "host", "x-amz-acl", "x-amz-content-sha256", "x-amz-date" };
            string canonicalHeaders =
                $"host:{host}\n" +
                "x-amz-acl:public-read\n" +
                $"x-amz-content-sha256:{payloadHash}\n" +
                $"x-amz-date:{amzDate}\n";
            string signedHeaders = string.Join(";", signedHeaderNames);

            string canonicalRequest = string.Join("\n",
                "PUT", canonicalUri, "", canonicalHeaders, signedHeaders, payloadHash);

            string credentialScope = $"{dateStamp}/{region}/{Service}/aws4_request";
            string stringToSign = string.Join("\n",
                "AWS4-HMAC-SHA256", amzDate, credentialScope, Hex(Sha256(canonicalRequest)));

            byte[] signingKey = GetSignatureKey(secretKey, dateStamp, region, Service);
            string signature = Hex(HmacSha256(signingKey, stringToSign));

            string authorization =
                $"AWS4-HMAC-SHA256 Credential={accessKey}/{credentialScope}, " +
                $"SignedHeaders={signedHeaders}, Signature={signature}";

            UnityEngine.Debug.Log($"BuildTools: uploading {objectKey} to Scaleway Object Storage...");

            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "PUT";
                req.ContentLength = fileInfo.Length;
                req.Headers["x-amz-acl"] = "public-read";
                req.Headers["x-amz-content-sha256"] = payloadHash;
                req.Headers["x-amz-date"] = amzDate;
                req.Headers["Authorization"] = authorization;

                using (var fs = File.OpenRead(filePath))
                using (var rs = req.GetRequestStream())
                    fs.CopyTo(rs);

                using var resp = (HttpWebResponse)req.GetResponse();
                bool ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
                if (ok)
                {
                    string downloadUrl = $"https://{host}/{objectKey}";
                    UnityEngine.Debug.Log($"BuildTools: upload succeeded — {fileInfo.Length / 1024 / 1024} MB\nDownload: {downloadUrl}");
                }
                else
                    UnityEngine.Debug.LogError($"BuildTools: upload failed — HTTP {(int)resp.StatusCode}");
                return ok;
            }
            catch (WebException ex)
            {
                string body = ex.Response != null
                    ? new StreamReader(ex.Response.GetResponseStream()!).ReadToEnd()
                    : ex.Message;
                UnityEngine.Debug.LogError($"BuildTools: upload failed — {body}");
                return false;
            }
        }

        static byte[] Sha256(string s)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        }

        static byte[] HmacSha256(byte[] key, string data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        static byte[] GetSignatureKey(string secretKey, string dateStamp, string region, string service)
        {
            byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
            byte[] kRegion = HmacSha256(kDate, region);
            byte[] kService = HmacSha256(kRegion, service);
            return HmacSha256(kService, "aws4_request");
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
