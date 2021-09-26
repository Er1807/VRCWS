using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VRCWSLibary
{
    public class SecurityContext
    {
        //Thanks Requi 
        public class RSAParametersWrapper{

            public static RSAParametersWrapper FromRSAParameters(RSAParameters rsa)
            {
                return new RSAParametersWrapper()
                {
                    Exponent = rsa.Exponent,
                    Modulus = rsa.Modulus,
                    P = rsa.P,
                    Q = rsa.Q,
                    DP = rsa.DP,
                    DQ = rsa.DQ,
                    InverseQ = rsa.InverseQ,
                    D = rsa.D
                };
            }

            public static RSAParameters FromRSAParameters(RSAParametersWrapper rsa)
            {
                return new RSAParameters()
                {
                    Exponent = rsa.Exponent,
                    Modulus = rsa.Modulus,
                    P = rsa.P,
                    Q = rsa.Q,
                    DP = rsa.DP,
                    DQ = rsa.DQ,
                    InverseQ = rsa.InverseQ,
                    D = rsa.D
                };
            }

            public byte[] Exponent;
            public byte[] Modulus;
            public byte[] P;
            public byte[] Q;
            public byte[] DP;
            public byte[] DQ;
            public byte[] InverseQ;
            public byte[] D;
        }

        private static RSACryptoServiceProvider provider;

        public static RSAParametersWrapper privKey;
        public static RSAParametersWrapper pubKey;

        public static Dictionary<string, RSAParameters> keysPerUser = new Dictionary<string, RSAParameters>();

        private static void CreateKeys(){
            provider = new RSACryptoServiceProvider(2048);

            privKey = RSAParametersWrapper.FromRSAParameters(provider.ExportParameters(true));
            pubKey = RSAParametersWrapper.FromRSAParameters(provider.ExportParameters(false));

            MelonLogger.Msg("Created new keys");

        }

        public static void AcceptPubKey(string pubkey, string userId)
        {
            keysPerUser[userId] = JsonConvert.DeserializeObject<RSAParameters>(pubkey);

            MelonLogger.Msg($"Accepted key for user {userId}");
            SaveKeys();
        }


        public static void SaveKeys()
        {
            Directory.CreateDirectory("UserData/VRCWS");
            File.WriteAllText("UserData/VRCWS/privkey.json", JsonConvert.SerializeObject(privKey));
            File.WriteAllText("UserData/VRCWS/pubkeydictionary.json", JsonConvert.SerializeObject(keysPerUser));
            MelonLogger.Msg("Saving keys");
        }

        public static void LoadKeys()
        {
            if (File.Exists("UserData/VRCWS/privkey.json"))
            {
                privKey = JsonConvert.DeserializeObject<RSAParametersWrapper>(File.ReadAllText("UserData/VRCWS/privkey.json"));
                keysPerUser = JsonConvert.DeserializeObject<Dictionary<string, RSAParameters>>(File.ReadAllText("UserData/VRCWS/pubkeydictionary.json"));
                MelonLogger.Msg(keysPerUser.Count);
                provider = new RSACryptoServiceProvider();

                provider.ImportParameters(RSAParametersWrapper.FromRSAParameters(privKey));

                pubKey = RSAParametersWrapper.FromRSAParameters(provider.ExportParameters(false));

            }
            else
            {
                CreateKeys();
                SaveKeys();
            }
            MelonLogger.Msg("Loaded keys");
        }

        public static string GetPublicKeyAsJsonString()
        {
            return JsonConvert.SerializeObject(pubKey);
        }

        public static void Sign(Message msg)
        {
            msg.Signature = Sign(msg.Content);
        }

        public static string Sign(string data)
        {
            return GetBase64FromBytes(provider.SignData(GetStringASBytes(data), SHA256.Create()));
        }
        public static bool Verify(Message msg)
        {
            if (!keysPerUser.ContainsKey(msg.Target))
                return false;

            return Verify(msg.Content, GetBytesFromBase64(msg.Signature), keysPerUser[msg.Target]);

        }
        public static bool Verify(string data, byte[] signature, RSAParameters remotePubkey)
        {
            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(remotePubkey);
            return rsa.VerifyData(GetStringASBytes(data), SHA256.Create(), signature);

        }

        private static byte[] GetStringASBytes(string data)
        {
            if (data == null) data = "";
            return Encoding.UTF8.GetBytes(data);
        }
        private static string GetBytesAsString(byte[] data)
        {
            return Encoding.UTF8.GetString(data);
        }
        private static byte[] GetBytesFromBase64(string data)
        {
            if (data == null) data = "";
            return Convert.FromBase64String(data);
        }
        private static string GetBase64FromBytes(byte[] data)
        {
            return Convert.ToBase64String(data);
        }
    }
}
