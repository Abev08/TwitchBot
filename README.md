# AbevBot - Another Twitch Bot

Two versions are available:
 - **Console** - Mostly a console window with added SFML package for low level graphical interface. Can easily create RenderWindow in which you can display shapes / sprites with textures and create animations. With few tweaks (like changing ngrok.exe reference to ngrok.sh) can be built for operating systems other than Windows.
 - **WPF** - Almost the same as Console version but uses Windows Presentation Foundation (WPF). WPF is used instead of SFML, which grants higher level of abstraction on graphical interface (like premade user controls). Can display videos and play audio clips. Because WPF is used can be built only for Windows.

The bots are running locally on PC that runs .exe and communicates only with needed apis.\
Bots use console window to inform the user what bot is doing.
