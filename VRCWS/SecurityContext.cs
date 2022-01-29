using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

        private static void CreateKeys(){
            provider = new RSACryptoServiceProvider(2048);

            MelonLogger.Msg("Created new keys");
            Directory.CreateDirectory("UserData/VRCWS");
            File.WriteAllText("UserData/VRCWS/privkey.json", JsonConvert.SerializeObject(RSAParametersWrapper.FromRSAParameters(provider.ExportParameters(true))));
            MelonLogger.Msg("Saving key");

        }


        public static void LoadKeys()
        {
            if (File.Exists("UserData/VRCWS/privkey.json"))
            {
                var privKey = JsonConvert.DeserializeObject<RSAParametersWrapper>(File.ReadAllText("UserData/VRCWS/privkey.json"));
                provider = new RSACryptoServiceProvider();

                provider.ImportParameters(RSAParametersWrapper.FromRSAParameters(privKey));
            }
            else
            {
                CreateKeys();
            }
            MelonLogger.Msg("Loaded keys");
        }

        public static X509Certificate2 GetCertificate(string userID)
        {
            if (!File.Exists($"UserData/VRCWS/{userID}.cert"))
            {
                return null;
            }

            return new X509Certificate2(File.ReadAllBytes($"UserData/VRCWS/{userID}.cert"));
        }

        public static void SaveCertificate(string userID, X509Certificate2 certificate)
        {
            File.WriteAllBytes($"UserData/VRCWS/{userID}.cert", certificate.RawData);
        }

        public static CertificateRequest CreateCSR(string userID)
        {
            
            CertificateRequest req = new CertificateRequest(
                                    $"CN={userID}",
                                    RSA.Create(provider.ExportParameters(true)),
                                    HashAlgorithmName.SHA256,
                                    RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));


            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                    false));

            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                    new Oid("1.3.6.1.5.5.7.3.2")
                    },
                    true));

            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
            req.CreateSigningRequest();
            return req;
        }

        public static string PemEncodeSigningRequest(CertificateRequest request)
        {
            byte[] pkcs10 = request.CreateSigningRequest();
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("-----BEGIN CERTIFICATE REQUEST-----");

            string base64 = Convert.ToBase64String(pkcs10);

            int offset = 0;
            const int LineLength = 64;

            while (offset < base64.Length)
            {
                int lineEnd = Math.Min(offset + LineLength, base64.Length);
                builder.AppendLine(base64.Substring(offset, lineEnd - offset));
                offset = lineEnd;
            }

            builder.AppendLine("-----END CERTIFICATE REQUEST-----");
            return builder.ToString();
        }

        public static string Sign(string data)
        {
            return GetBase64FromBytes(provider.SignData(GetStringASBytes(data), SHA256.Create()));
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
