# VRCWS

VRCWS is a libary to enable communication between multiple clients in VRC based on an event system.

For an example code look in the [TextChat.cs](https://github.com/Er1807/VRCTextChat/blob/main/Client/TextChat.cs) file of another project of mine or [VRCWSLibaryIntegration.cs](https://github.com/Er1807/FreezeFrame/blob/main/VRCWSLibaryIntegration.cs) of FreezeFrame

As a general note of caution. Don't trust that the origin or reciever of the message if signing is not enabled. Everyone can say they are someone else.
When connecting to any world a hash of the world instance is send to the server. By default unless mods opt-in to recieve events from anyone only people in the same world (or that can see the instance id) can send you events. This is a setting per event.

Also unless opt-in events are only forwarded to event handlers if the are trusted. This works by public key signing. 

To trust a new client activate "Accept public key" (disabled on startup)

![image](https://user-images.githubusercontent.com/20169013/134819167-16e66e2a-3907-45ec-9c76-a690096a21cd.png)

Then ask the other person to send their publik key(both players need to be in the same world)

![image](https://user-images.githubusercontent.com/20169013/134819203-c0f6c2b5-8a27-4602-87fd-4692dbeea1b9.png)

Afterwards you need to accept their key. When you are done you should disable the setting again

![image](https://user-images.githubusercontent.com/20169013/134819221-abd1324c-5e58-43db-a363-c03e0faba54c.png)


It is possible to create a new Instance of Client and therefore connect to another server. Also the main server for the default client can be changed.

A docker server version can be found at https://hub.docker.com/r/er1807/vrcws. When hosting your own server (maybe even inside a vpn) you can increase the security.

But you are free to use wss://vrcws.er1807.de/VRC 

Just don't be stupid. Also I can't say that the server will be running 24/7.

You can change your connected server and if you wanna be connected in the Molonsettings. Not that mods building upon this mod can use this default or specify their own server.
![image](https://user-images.githubusercontent.com/20169013/134067335-9025f8af-486f-4d09-80c6-a84ddad19637.png)
