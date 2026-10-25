#!/bin/sh
# Builds src/plugin/Assets/megaphonk-music.bundle from src/plugin/Music/*.mp3 with the game's Unity version
UNITY="${UNITY:-D:/Unity/2023.2.22f1/Editor/Unity.exe}"
cd "$(dirname "$0")" && "$UNITY" -batchmode -nographics -quit -projectPath . -buildTarget StandaloneWindows64 -executeMethod BuildMusicBundle.Build -logFile -
