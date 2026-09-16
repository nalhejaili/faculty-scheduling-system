using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TrainerScheduler.Services
{
    public static partial class LicenseService
    {


        private static bool TryDecodeSerial(
            string serial,
            out LicensePayload payload,
            out string error)
        {
            payload = null!;
            error = null!;

            try
            {
                string base64 = serial.Replace('-', '+').Replace('_', '/');
                switch (base64.Length % 4)
                {
                    case 2: base64 += "=="; break;
                    case 3: base64 += "="; break;
                }

                byte[] finalBytes = Convert.FromBase64String(base64);

                if (finalBytes.Length <= 8)
                {
                    error = "The serial key is too short and invalid.";
                    return false;
                }

                int payloadLength = finalBytes.Length - 8;
                byte[] payloadBytes = new byte[payloadLength];
                byte[] checksumBytes = new byte[8];

                Buffer.BlockCopy(finalBytes, 0, payloadBytes, 0, payloadLength);
                Buffer.BlockCopy(finalBytes, payloadLength, checksumBytes, 0, 8);

                byte[] keyBytes = Encoding.UTF8.GetBytes(SecretKey);
                byte[] toHash = new byte[keyBytes.Length + payloadBytes.Length];
                Buffer.BlockCopy(keyBytes, 0, toHash, 0, keyBytes.Length);
                Buffer.BlockCopy(payloadBytes, 0, toHash, keyBytes.Length, payloadBytes.Length);

                byte[] hash;
                using (SHA256 sha = SHA256.Create())
                {
                    hash = sha.ComputeHash(toHash);
                }

                for (int i = 0; i < 8; i++)
                {
                    if (hash[i] != checksumBytes[i])
                    {
                        error = "The serial key is invalid (signature verification failed).";
                        return false;
                    }
                }

                string payloadString = Encoding.UTF8.GetString(payloadBytes);
                string[] parts = payloadString.Split('|');
                if (parts.Length != 3)
                {
                    error = "The serial key format is not recognized.";
                    return false;
                }

                string machineId = parts[0];

                if (!DateTime.TryParseExact(
                        parts[1],
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime expiry))
                {
                    error = "The expiry date embedded in the serial key is invalid.";
                    return false;
                }

                string plan = parts[2];

                payload = new LicensePayload(machineId, expiry, plan);
                return true;
            }
            catch
            {
                error = "An error occurred while reading the serial key.";
                return false;
            }
        }

        public static string GetMachineId()
        {
            try
            {
                string machineName = Environment.MachineName ?? "";
                string userName = Environment.UserName ?? "";
                string osVersion = Environment.OSVersion.VersionString ?? "";

                string raw = "TrainerScheduler|" + machineName + "|" + userName + "|" + osVersion;

                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    StringBuilder sb = new StringBuilder(16);
                    for (int i = 0; i < 8; i++)
                    {
                        sb.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                    }
                    return sb.ToString();
                }
            }
            catch
            {
                return Environment.MachineName ?? "UNKNOWN";
            }
        }
    }
}
