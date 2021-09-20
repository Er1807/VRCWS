# VRCWS

VRCWS is a libary to enable communication between multiple clients in VRC based on an event system.

For an example code look in the [TextChat.cs](https://github.com/Er1807/VRCTextChat/blob/main/Client/TextChat.cs) file of another project of mine or [VRCWSLibaryIntegration.cs](https://github.com/Er1807/FreezeFrame/blob/main/VRCWSLibaryIntegration.cs) of FreezeFrame

As a general note of caution. Don't trust that the origin or reciever of the message is the real one. Everyone can say they are someone else.
Currently on the first starts it needs around 3-4 attempts to connect successfully. No idea why -_-

It is possible to create a new Instance of Client and therefore connect to another server. Also the main server for the default client can be changed.

A docker server version can be found at https://hub.docker.com/r/er1807/vrcws. When hosting your own server (maybe even inside a vpn) you can increase the security.

But you are free to use wss://vrcws.er1807.de/VRC 

Just don't be stupid. Also I can't say that the server will be running 24/7.