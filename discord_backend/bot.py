import discord
from discord.ext import commands
from discord import app_commands
import os
import random
import requests
import re
from yt import YoutubeDownloader


# config variables
BOT_TOKEN = 'BOT TOKEN'
request_channel_id = 1234567890
clips_channel_id = 1234567890
moderator_role_id = 1234567890
YoutubeAllowed = True
CustomFileAllowed = True
YoutubeTimeLimitInSeconds = 15

# intents
intents = discord.Intents.default()
intents.guild_messages = True
intents.guild_reactions = True
intents.guilds = True
intents.message_content = True

bot = commands.Bot(command_prefix='!', intents=intents)
tree = bot.tree

youtube = YoutubeDownloader()

@bot.event
async def on_ready():
    print(f'Logged in as {bot.user.name}')

    # add the commands to the command tree
    if YoutubeAllowed:
        bot.add_command(ytrequest)
    if CustomFileAllowed:
        bot.add_command(filerequest)

    bot.add_command(refresh)
    bot.add_command(clear)

    # remove commands that are not allowed
    if not YoutubeAllowed:
        bot.remove_command('ytrequest')
    if not CustomFileAllowed:
        bot.remove_command('filerequest')
    

@commands.hybrid_command()
async def ytrequest(ctx, url: str, start: int = -1, end: int = -1):
    # check if the command is executed in the request channel
    if ctx.channel.id != request_channel_id:
        # ignore the command if it is not executed in the request channel
        return
    
    pattern = r'https?://(www\.)?(youtube\.com|youtu\.be)'

    if YoutubeAllowed:
        # check if the url is a youtube video
        if re.search(pattern, url):
            
            # check if the difference between start and end is less than the time limit
            if start != -1 and end != -1:
                if end - start > YoutubeTimeLimitInSeconds:
                    return
            message = await ctx.send('Processing request...')

            # check if the youtube video is longer than the time limit if start and end are not provided
            if youtube.get_video_length(url) > YoutubeTimeLimitInSeconds:
                return
            
            # send hidden status   
            
            
            # download the video and send it
            if start == -1 and end == -1:
                random_name = youtube.download_mp4(url)
            else:
                random_name = youtube.download_mp4_cut(url, start, end)

            file = discord.File(f'./{random_name}.mp4')
            await message.edit(content = '', attachments = [file])
            # remove the file
            file.close()
            os.remove(f'./{random_name}.mp4')
            await message.add_reaction('✅')
            await message.add_reaction('❌')

            
        else:
            await ctx.send('Sorry, the file is not a mp4 file!')
    else:
        await ctx.send('Sorry, I am not allowed to send files!')


@commands.hybrid_command()
async def filerequest(ctx, file: discord.Attachment):

    # check if the command is executed in the request channel
    if ctx.channel.id != request_channel_id:
        # ignore the command if it is not executed in the request channel
        return

    if CustomFileAllowed:
        if file.filename.endswith('.mp4'):
            # send the video and add two reactions to the message - a tick and a cross
            # download the file with a random name temporarily and send it and then delete it
            random_name = int(random.random()*10000)
            await file.save(f'./{random_name}.mp4')
            file = discord.File(f'./{random_name}.mp4')
            message = await ctx.send('', file=file)
            # remove the file

            file.close()
            os.remove(f'./{random_name}.mp4')
            await message.add_reaction('✅')
            await message.add_reaction('❌')
        else:
            await ctx.send('Sorry, the file is not a mp4 file!')
    else:
        await ctx.send('Sorry, I am not allowed to send files!')


# refresh commands
@commands.hybrid_command()
async def refresh(ctx):
    message = await ctx.send('Refreshing commands...')

    # sync the commands
    await tree.sync()

    await message.edit(content='Commands have been refreshed!')

# clear all messages in the clips channel
@commands.hybrid_command()
async def clear(ctx):
    if ctx.channel.id == request_channel_id:
        await ctx.channel.purge()
    else:
        await ctx.send('Sorry, this command can only be executed in the clips request channel!')

# add a event listener for the reaction add event
@bot.event
async def on_reaction_add(reaction, user):
    # check if the reaction is added in the clips channel
    if reaction.message.channel.id == request_channel_id:
        # check if the reaction is added by the bot
        if user.id == bot.user.id:
            return
        
        # check if the reaction is added to a message with a file
        if len(reaction.message.attachments) == 0:
            return
        
        # check if the reaction is added to a message that the bot sent
        if reaction.message.author.id != bot.user.id:
            return
        
        # check if the user has the moderator role
        if moderator_role_id not in [role.id for role in user.roles]:
            return

        # check if the reaction is a tick
        if reaction.emoji == '✅':
            # send a message to the clips channel

            # get channel by id
            clips_channel = bot.get_channel(clips_channel_id)

            # get attachments from the message
            attachments = reaction.message.attachments
            


            # download the file with a random name temporarily and send it and then delete it
            file_url = attachments[0]
            random_name = int(random.random()*10000)

            # download the file to /tmp with a random name
            file = requests.get(file_url)
            with open(f'./{random_name}.mp4', 'wb') as f:
                f.write(file.content)
            
            file = discord.File(f'./{random_name}.mp4')
            await clips_channel.send(f'From {user.mention}:', file=file)

            file.close()

            # remove the file
            os.remove(f'./{random_name}.mp4')
            
            # remove the message
            await reaction.message.delete()

        # check if the reaction is a cross
        if reaction.emoji == '❌':
            # remove the message
            await reaction.message.delete()

# Run the bot
bot.run(BOT_TOKEN)