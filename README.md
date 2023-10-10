# AbevBot - Another Twitch Bot
[![Build Status](https://github.com/Abev08/TwitchBot/actions/workflows/build.yml/badge.svg)](https://github.com/Abev08/TwitchBot/actions/workflows/build.yml)  
  
Programmed with C# on .NET 7.  
It's modular - the functionalities are split between .cs files.  
Uses Windows Presentation Foundation (WPF, an UI framework) to open bot configuration window. Can also be used as OBS input for for example video clips. Because WPF is used can be built only for Windows.
<br><br>

## **Features**
- Integrates Twitch IRC chat:
  - Reads messages,
  - Can replay to configured keys in read messages (automated responses),
  - Detects custom rewards messages which require redeemer to type something in chat,
  - Detects bits messages which require cheerer to type something in chat,
  - Detects badges owned by message author (MOD, SUB, VIP, etc.),
  - Detects bots messages,
  - Detects special messages:
    - Subscriptions,
    - Gifted subscriptions (distinguishes between random and to specific user),
    - Upgrade from prime subscription,
    - Announcements,
    - Raids.
  - Detects bans and timeouts,
  - Detects changes in chat emote-only mode,
  - Sends periodic messages configured in Config.ini file,
  - Can be run without broadcaster permissions.

- Subscribes to EventSub (Twitch events):
  - Subscribes to follow notification event,
  - Subscribes to subscriptions notifications events (there are multiple subscription types),
  - Subscribes to bits cheer event,
  - Subscribes to channel points custom rewards redemptions events,
  - Uses StreamElements api for TTS generation.

- Implements chat interactions / commands:
  - Text To Speech (`!tts`),
  - Gambling (`!gamba`), <p align="center"><img src="ReadmeImages/MinigameGamba.png" width=200 alt="Gamba minigame"></p>
  - Fighting (`!fight`), <p align="center"><img src="ReadmeImages/MinigameFight.png" width=200 alt="Gamba minigame"></p>
  - Backseat points (`!point`) - the streamer can reward the chatter for helping, <p align="center"><img src="ReadmeImages/MinigamePoint.png" width=200 alt="Gamba minigame"></p>
  - Rude points (`!rude`) - chatters can point a chatter for being rude, <p align="center"><img src="ReadmeImages/MinigameRude.png" width=200 alt="Gamba minigame"></p>
  - Vanish (`!vanish`) - self timeout for the chatter, also deletes chatter messages.
  - Hug (`!hug`) - just a friendly hug to other person.

- Bot configuration is carried out in:
  - Secrets.ini (Twitch app data: customer ID, passwords, etc.),
  - Config.ini (channel name, bot notifications configuration, etc.),
  - ResponseMessages.csv (automated response messages).
<br><br>

## **Required information in Secrets.ini**
The file will be generated automatically the first time the bot is run.
 - Bot's name (`Name`) - Name of the registered application on https://dev.twitch.tv/console/apps.
 - Bot's client ID (`CustomerID`) - Customer ID of the registered application on https://dev.twitch.tv/console/apps.
 - Bot's password (`Password`) - Customer password of the registered application on https://dev.twitch.tv/console/apps.
<p align="center"><img src="ReadmeImages/BotLogin.png" height="400" alt="Bot's Nick, ClientID and Password"></p>  

## **Required information in Config.ini**
The file will be generated automatically the first time the bot is run.
 - Channel name (`ChannelName`) - Name of the channel to connect to.
<br><br><br><br>

[Deprecated source code explanation (maybe it will be updated someday)](SourceCodeExplanation.md)
