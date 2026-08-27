using System;
using System.IO;
using System.Text;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>Replaces a payload and manifest as one rollback-safe publication.</summary>
    public static class PhysicsArtifactPairWriter
    {
        /// <summary>
        /// Stages both files before touching the destination and restores the previous pair if
        /// either final move fails. Callers must validate the content before invoking this method.
        /// </summary>
        public static void Write(
            string payloadPath,
            byte[] payload,
            string manifestPath,
            string manifestJson)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (manifestJson == null) throw new ArgumentNullException(nameof(manifestJson));
            if (string.IsNullOrEmpty(payloadPath)) throw new ArgumentException("A payload path is required.", nameof(payloadPath));
            if (string.IsNullOrEmpty(manifestPath)) throw new ArgumentException("A manifest path is required.", nameof(manifestPath));

            string token = Guid.NewGuid().ToString("N");
            string payloadTemp = payloadPath + ".pair-" + token + ".tmp";
            string manifestTemp = manifestPath + ".pair-" + token + ".tmp";
            string payloadBackup = payloadPath + ".pair-" + token + ".bak";
            string manifestBackup = manifestPath + ".pair-" + token + ".bak";
            bool payloadBackedUp = false;
            bool manifestBackedUp = false;
            bool payloadInstalled = false;
            bool manifestInstalled = false;

            try
            {
                File.WriteAllBytes(payloadTemp, payload);
                File.WriteAllText(manifestTemp, manifestJson, new UTF8Encoding(false));

                if (File.Exists(payloadPath))
                {
                    File.Move(payloadPath, payloadBackup);
                    payloadBackedUp = true;
                }

                if (File.Exists(manifestPath))
                {
                    File.Move(manifestPath, manifestBackup);
                    manifestBackedUp = true;
                }

                File.Move(payloadTemp, payloadPath);
                payloadInstalled = true;
                File.Move(manifestTemp, manifestPath);
                manifestInstalled = true;
            }
            catch
            {
                if (payloadInstalled) DeleteIfExists(payloadPath);
                if (manifestInstalled) DeleteIfExists(manifestPath);
                if (payloadBackedUp) File.Move(payloadBackup, payloadPath);
                if (manifestBackedUp) File.Move(manifestBackup, manifestPath);
                throw;
            }
            finally
            {
                DeleteIfExists(payloadTemp);
                DeleteIfExists(manifestTemp);
                DeleteIfExists(payloadBackup);
                DeleteIfExists(manifestBackup);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
