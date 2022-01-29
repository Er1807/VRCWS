using Newtonsoft.Json;
using System;

namespace Server
{
    public class Message
    {
        public Message() { }
        public Message(Message from) { ID = from.ID; }

        public string Method { get; set; }
        public string Target { get; set; }
        public string Content { get; set; }
        public Guid ID { get; set; } = Guid.NewGuid();
        public DateTime TimeStamp { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{Method} - {Target} - {Content}";
        }
        public T GetContentAs<T>()
        {
            try
            {

                return JsonConvert.DeserializeObject<T>(Content);
            }
            catch (Exception)
            {
                return default;
            }
        }
    }

    public class AcceptedMethod
    {
        public string Method { get; set; }
        public bool WorldOnly { get; set; }

        public override string ToString()
        {
            return $"{Method} - {WorldOnly}";
        }
    }

    public class LoginMessage
    {
        public string Signature { get; set; }
        public string Certificate { get; set; }
    }

    public class Strings
    {
        //Methods
        public const string ErrorString = "Error";

        public const string OnlineStatusString = "OnlineStatus";
        public const string MethodIsAcceptedString = "MethodIsAccepted";
        public const string MethodIsDeclined = "MethodIsDeclined";

        public const string LoginString = "Login";
        public const string RegisterString = "Register";
        public const string ConnectedString = "Connected";
        public const string RegisterChallengeString = "RegisterChallenge";
        public const string RegisterChallengeCompletedString = "RegisterChallengeCompleted";
        public const string RegisterChallengeSuccessString = "RegisterChallengeSuccess";
        public const string IsOnlineString = "IsOnline";
        public const string DoesUserAcceptMethodString = "DoesUserAcceptMethod";
        public const string RequestFriendString = "RequestFriend";
        public const string AddFriendString = "AddFriend";
        public const string RemoveFriendString = "RemoveFriend";
        public const string GetFriendsString = "GetFriends";
        public const string SetWorldString = "SetWorld";


        public const string WorldUpdatedString = "WorldUpdated";
        public const string AcceptMethodString = "AcceptMethod";
        public const string RemoveMethodString = "RemoveMethod";
        public const string MethodsUpdatedString = "MethodsUpdated";


        //Content
        public const string OnlineString = "Online";
        public const string OfflineString = "Offline";

        //Contents Error
        public const string InvalidMessageString = "InvalidMessage";
        public const string ToManyMethodsString = "ToManyMethods";
        public const string MethodAlreadyExistedString = "MethodAlreadyExisted";
        public const string RatelimitedString = "Ratelimited";
        public const string MessageToLargeString = "MessageTolarge";
        public const string NoTargetProvidedString = "NoTargetProvided";
        public const string UserOfflineString = "UserOffline";
        public const string MethodNotAceptedString = "MethodNotAcepted";
        public const string NoFriendsString = "NoFriends";
        public const string LoginFirstString = "LoginFirst";
        public const string RegisterChallengeFailedString = "RegisterChallengeFailed";
        public const string NoCertificateProvidedString = "No Certificate provided";
        public const string AlreadyConnectedString = "AlreadyConnected";
        public const string InvalidCertificateString = "InvalidCertificate";
    }

}
