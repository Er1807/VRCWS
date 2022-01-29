using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    class CertificateValidation
    {

        private static X509Certificate2 root;

        private static X509Certificate2 GenerateAuthority()
        {
            RSA parent = RSA.Create(4096);

            CertificateRequest parentReq = new CertificateRequest(
                    "CN=VRCWS Issuing Authority",
                    parent,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

            parentReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));

            parentReq.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(parentReq.PublicKey, false));

            X509Certificate2 parentCert = parentReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-45),
                DateTimeOffset.UtcNow.AddDays(365));

            return parentCert;
        }

        private static async Task<X509Certificate2> GetOrCreateAuthority()
        {
            if (root == null)
            {
                string caAsString = await Redis.Get("CA");
                if (caAsString != null)
                {
                    root = new X509Certificate2(Convert.FromBase64String(caAsString));
                    ExportForOpenSSL();
                }

            }

            if (root != null)
            {
                if (root.NotAfter > DateTime.Now)
                {
                    root = null;
                }

            }

            if (root == null)
            {
                root = GenerateAuthority();
                await Redis.Set("CA", Convert.ToBase64String(root.RawData));
                ExportForOpenSSL();
            }

            return root;
        }

        private static void ExportForOpenSSL()
        {
            File.WriteAllBytes("root.pfx", root.Export(X509ContentType.Pfx));
            Process.Start("openssl pkcs12 -in root.pfx -passin pass: -clcerts -nokeys -out root.crt").WaitForExit();
            Process.Start("openssl pkcs12 -in root.pfx -passin pass: -nocerts -nodes -out root.key").WaitForExit();
        }

        //Currently not possible because CSR cant be deserialised
        public static X509Certificate2 SignCSR(CertificateRequest request, string userID)
        {

            string userIDInRequest = request.SubjectName.Name.Replace("CN=", "");
            if (userIDInRequest != userID)
                return null;

            return request.Create(
                        GetOrCreateAuthority().Result,
                        DateTimeOffset.UtcNow.AddDays(-1),
                        DateTimeOffset.UtcNow.AddDays(90),
                        Guid.NewGuid().ToByteArray());
        }
        private static int counter = 0;
        public static X509Certificate2 SignCSR(string request, string userID)
        {
            int localCounter = Interlocked.Increment(ref counter);
            File.WriteAllText($"{localCounter}.csr", request);
            Process.Start($"openssl x509 -req -in {localCounter}.csr -days 90 -CA root.crt -CAkey root.key -set_serial {localCounter} -out {localCounter}.crt").WaitForExit();

            X509Certificate certificate = X509Certificate.CreateFromCertFile($"{localCounter}.crt");
            File.Delete($"{localCounter}.csr");
            File.Delete($"{localCounter}.crt");
            if (certificate.Subject.Replace("CN=", "") == userID)
                return new X509Certificate2(certificate);
            return null;
        }

        public static bool ValidateCertificateAgainstRoot(X509Certificate2 certificate, string userID, byte[] signedUserID)
        {
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.CustomTrustStore.Clear();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(GetOrCreateAuthority().Result);

            if (!chain.Build(certificate))
                return false;

            string userIDInCertificate = certificate.Subject.Replace("CN=", "");
            if (userIDInCertificate != userID)
                return false;

            return certificate.GetRSAPublicKey().VerifyData(Encoding.UTF8.GetBytes(userIDInCertificate), signedUserID, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        }

    }
}
