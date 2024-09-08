
import yt_dlp
from yt_dlp.utils import download_range_func
import random

class YoutubeDownloader:
    def __init__(self) -> None:
        # disable yt-dlp logging
        yt_dlp.utils.bypass = True
        pass  

    def download_mp4(self, url:str) -> None:
        random_name = int(random.random()*10000)
        ydl_opts = {
            'format': 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best',
            'outtmpl': f'./{random_name}.mp4'
        }
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.download([url])

        return random_name

    def download_mp4_cut(self, url: str, start: int, end: int) -> None:
        random_name = int(random.random()*10000)
        ydl_opts = {
            'format': 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best',
            'outtmpl': f'./{random_name}.mp4',
            'download_range': download_range_func(start, end)
        }
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.download([url])

        return random_name
    
    def get_video_length(self, url: str) -> int:
        random_name = int(random.random()*10000)
        ydl_opts = {
            'format': 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best',
            'outtmpl': f'./{random_name}.mp4'
        }
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info = ydl.extract_info(url, download=False)
            return info['duration']
        