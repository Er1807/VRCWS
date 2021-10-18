
using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Math;
namespace VRCWSLibary
{
    public class SecurityContext
    {
        public class RSAParametersWrapper
        {

            public static RSAParametersWrapper FromRSAParameters(RsaKeyParameters rsa)
            {
                return new RSAParametersWrapper()
                {
                    Exponent = rsa.Exponent.ToString(16),
                    Modulus = rsa.Modulus.ToString(16)
                };
            }

            public static RsaKeyParameters FromRSAParameters(RSAParametersWrapper rsa, bool isPrivate)
            {
                return new RsaKeyParameters(isPrivate, new BigInteger(rsa.Modulus, 16), new BigInteger(rsa.Exponent, 16));
            }

            public string Exponent;
            public string Modulus;
        }

        private static RsaKeyParameters publicKey { get; set; }
        public static RsaKeyParameters privateKey { get; set; }

        public static Dictionary<string, RSAParametersWrapper> keysPerUser = new Dictionary<string, RSAParametersWrapper>();

        private static void CreateKeys(){
            var kpgen = new RsaKeyPairGenerator();

            kpgen.Init(new KeyGenerationParameters(new SecureRandom(new CryptoApiRandomGenerator()), 2048));
            var keyPair = kpgen.GenerateKeyPair();

            privateKey = (RsaKeyParameters)keyPair.Private;
            publicKey = (RsaKeyParameters)keyPair.Public;

            MelonLogger.Msg("Created new keys");

        }

        public static void AcceptPubKey(string pubkey, string userId)
        {
            try
            {

                keysPerUser[userId] = JsonConvert.DeserializeObject<RSAParametersWrapper>(pubkey);
                MelonLogger.Msg($"Accepted key for user {userId}");
                SavePublicKeys();
            }
            catch (Exception)
            {
                MelonLogger.Msg("Invalid Json Package for PubKey");
            }

        }


        public static void SaveKeys()
        {
            Directory.CreateDirectory("UserData/VRCWS");
            File.WriteAllText("UserData/VRCWS/privatekey.json", JsonConvert.SerializeObject(RSAParametersWrapper.FromRSAParameters(privateKey)));
            File.WriteAllText("UserData/VRCWS/publickey.json", JsonConvert.SerializeObject(RSAParametersWrapper.FromRSAParameters(publicKey)));
            MelonLogger.Msg("Saving keys");
        }
        public static void SavePublicKeys()
        {
            Directory.CreateDirectory("UserData/VRCWS");
            File.WriteAllText("UserData/VRCWS/publickeydictionary.json", JsonConvert.SerializeObject(keysPerUser));
            MelonLogger.Msg("Saving public keys");
        }



        public static void LoadKeys()
        {
            if (File.Exists("UserData/VRCWS/privatekey.json") && File.Exists("UserData/VRCWS/publickey.json"))
            {
                privateKey = RSAParametersWrapper.FromRSAParameters(JsonConvert.DeserializeObject<RSAParametersWrapper>(File.ReadAllText("UserData/VRCWS/privatekey.json")), true);
                publicKey = RSAParametersWrapper.FromRSAParameters(JsonConvert.DeserializeObject<RSAParametersWrapper>(File.ReadAllText("UserData/VRCWS/publickey.json")), false);

            }
            else
            {
                CreateKeys();
                SaveKeys();
            }

            if (File.Exists("UserData/VRCWS/publickeydictionary.json"))
            {
                keysPerUser = JsonConvert.DeserializeObject<Dictionary<string, RSAParametersWrapper>>(File.ReadAllText("UserData/VRCWS/publickeydictionary.json"));
                MelonLogger.Msg($"Loaded {keysPerUser.Count} public keys");
            }

            MelonLogger.Msg("Loaded keys");
        }

        public static string GetPublicKeyAsJsonString()
        {
            return JsonConvert.SerializeObject(RSAParametersWrapper.FromRSAParameters(publicKey));
        }

        public static void Sign(Message msg)
        {
            msg.Signature = Sign(GetMessageAsString(msg));
        }

        public static string Sign(string data)
        {
            ISigner sig = SignerUtilities.GetSigner("SHA256withRSA");

            sig.Init(true, privateKey);
            MelonLogger.Msg(data);
            var bytes = GetStringASBytes(data);

            sig.BlockUpdate(bytes, 0, bytes.Length);
            byte[] signature = sig.GenerateSignature();
            return GetBase64FromBytes(signature);
        }
        public static bool Verify(Message msg)
        {
            if (!keysPerUser.ContainsKey(msg.Target)) {
                MelonLogger.Msg("Key not found");
                return false;
            }
            if (DateTime.UtcNow > msg.TimeStamp.AddSeconds(30))
            {
                MelonLogger.Msg("Message expired");
                return false;
            }
            return Verify(GetMessageAsString(msg), GetBytesFromBase64(msg.Signature), keysPerUser[msg.Target]);

        }
        public static bool Verify(string data, byte[] signature, RSAParametersWrapper remotePubkey)
        {
            ISigner signer = SignerUtilities.GetSigner("SHA256withRSA");

            signer.Init(false, RSAParametersWrapper.FromRSAParameters(remotePubkey, false));

            MelonLogger.Msg(data);
            var msgBytes = GetStringASBytes(data);


            signer.BlockUpdate(msgBytes, 0, msgBytes.Length);

            return signer.VerifySignature(signature);
        }

        private static string GetMessageAsString(Message msg)
        {
            return $"{msg.Method}:{msg.Content}:{msg.TimeStamp.Ticks}";
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
