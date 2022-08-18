﻿using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace TwitchBotWPF
{
    public partial class MainWindow : Window
    {
        [DllImport("Kernel32")]
        public static extern void AllocConsole();

        [DllImport("Kernel32")]
        public static extern void FreeConsole();

        public MainWindow()
        {
            InitializeComponent();

            AllocConsole();
            this.Closing += (sender, e) => FreeConsole(); // Free console window on program close
            ConsoleWarning(">> Hi. I'm AbevBot.");

            // Read Config.ini
            if (Config.ParseConfigFile())
            {
                FreeConsole();
                return;
            }

            Events.Start(); // Start events bot
            Chat.Start(); // Start chat bot

            btnTestTTS.Click += (sender, e) => Events.GetTTS(tbText.Text); // On button click get TTS audio and play it

            // Automatically set source to null after video ended
            VideoPlayer.MediaEnded += (sender, e) => VideoPlayer.Source = null;
            // Take control over video player
            VideoPlayer.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
            VideoPlayer.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;

            // Wait for window to be loaded (visible) to start a demo video
            this.Loaded += (sender, e) =>
            {
                VideoPlayer.Source = new Uri(@"file_example_MP4_480_1_5MG.mp4", UriKind.Relative);
                VideoPlayer.Play();
            };
        }

        public static void ConsoleWarning(string text, ConsoleColor color = ConsoleColor.DarkRed)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public static void ConsoleWriteLine(string text)
        {
            Console.WriteLine(text.ToString());
        }
    }
}
