using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using static Server.Strings;

namespace Server.Middlewares
{
    class AuthentificationMiddleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            if (msg.Method == RegisterString)
            {
                userVRCWS.userID = msg.Target;
                userVRCWS.randomText = Guid.NewGuid().ToString();
                await userVRCWS.Send(new Message(msg) { Method = RegisterChallengeString, Content = userVRCWS.randomText });
                return;
            }

            if (msg.Method == RegisterChallengeCompletedString)
            {
                await Task.Delay(5000);
                if ((await VrcApi.QueueGetUserBio(userVRCWS.userID)).Contains(userVRCWS.randomText))
                {
                    X509Certificate2 certificate = CertificateValidation.SignCSR(msg.Content, userVRCWS.userID);


                    await userVRCWS.Send(new Message(msg) { Method = RegisterChallengeSuccessString, Content = Convert.ToBase64String(certificate.RawData) });
                }
                else
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = RegisterChallengeFailedString });
                }
                return;
            }

            if (msg.Method == LoginString)
            {
                userVRCWS.userID = msg.Target;
                LoginMessage loginMessage = msg.GetContentAs<LoginMessage>();
                if (loginMessage == null)
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = NoCertificateProvidedString });
                    return;
                }
                if (VRCWS.userIDToVRCWS.ContainsKey(userVRCWS.userID))
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = AlreadyConnectedString });
                    return;
                }
                X509Certificate2 cert = new X509Certificate2(Convert.FromBase64String(loginMessage.Certificate));

                if (!CertificateValidation.ValidateCertificateAgainstRoot(cert, userVRCWS.userID, Convert.FromBase64String(loginMessage.Signature)))
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = InvalidCertificateString });
                    return;
                }
                userVRCWS.authenticated = true;
                VRCWS.userIDToVRCWS[userVRCWS.userID] = userVRCWS;
                await userVRCWS.Send(new Message(msg) { Method = ConnectedString });
                await Redis.Increase("UniqueConnected", userVRCWS.userID);
                userVRCWS.UpdateStats();
                return;
            }

            if (!userVRCWS.authenticated)
            {
                await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = LoginFirstString });
                return;
            }

            await CallNext(userVRCWS, e, msg);
        }
    }
}
