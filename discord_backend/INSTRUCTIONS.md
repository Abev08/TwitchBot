# INSTRUCTIONS.md

## Project Overview

This project is a seperate entity of the Abev Bot. 
The bot takes over the automation of clip requests on Discord and the moderation of these requested clips.
Users can request either mp4 files or YouTube videos that have a maximum length defined in the config.
It is written in Python and includes the following main components:
- `bot.py`: The main script for the bot.
- `yt.py`: The script for downloading and extracting information with yt-dlp.

## Prerequisites

Before running the project, ensure you have the following installed:
- Python 3.x
- pip (Python package installer)

## Installation

1. Install the required dependencies:
    ```sh
    pip install -r requirements.txt
    ```

## Configuration

1. You must set all variables to your needs!
    - Example in `bot.py`:
        ```
        BOT_TOKEN = 'BOT TOKEN' # Discord Bot Token.
        request_channel_id = 1234567890 # Channel ID for the Channel where Users can request clips.
        clips_channel_id = 1234567890 # Channel ID for the Channel where all clips are saved.
        moderator_role_id = 1234567890 # Role ID of the role, that can moderate the clips.
        YoutubeAllowed = True # Set it to True if youtube requests are allowed.
        CustomFileAllowed = True # Set it to True if uploading mp4 files for requests are allowed.
        YoutubeTimeLimitInSeconds = 15 # It explains itself - please do not ask cakez.
        ```

## Running the Bot

To run the bot, execute the following command:
```sh
python bot.py
```