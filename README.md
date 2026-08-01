<p align="center">
    <img src="https://github.com/space928/QPlayer/blob/main/QPlayer/Resources/Splash.png?raw=true" alt="QPlayer Logo" height="220">
</p>

[![Build Status](https://github.com/space928/QPlayer/actions/workflows/dotnet-desktop.yml/badge.svg?branch=main)](https://github.com/space928/QPlayer/actions/workflows/dotnet-desktop.yml)
[![GitHub License](https://img.shields.io/github/license/space928/QPlayer)](https://github.com/space928/QPlayer/blob/main/LICENSE)
[![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/space928/QPlayer/total)](https://github.com/space928/QPlayer/releases)
[![Documentation](https://img.shields.io/badge/Documentation-darkgreen?color=%2324aa2a&link=https%3A%2F%2Fspace928.github.io%2FQPlayer)](https://qplayer.eu/reference/)


QPlayer is a simple media player for theatre. It allows cue lists of sound tracks to be created and 
played. Media playback is handled by NAudio, providing a large range of supported media.

**Features:**
 - Playback of a range of audio types (wav, mp3, etc...)
 - Playback of multiple cues concurrently
 - Group cues
 - Fade in, fade out, and fade cues
 - Pausing and preloading cues
 - Cue pre-delays
 - Per-cue EQ and a global limiter
 - OSC support
 - Super reliable audio engine
 - A detailed user manual
 - An easy to use editor, with undo/redo, drag & drop, batch editing, and more...


![Application screenshot](https://github.com/space928/QPlayer/assets/15130114/1a63eaaa-2c13-48e4-be0e-e33b5921bb41)

## Installation
The latest stable release of QPlayer can be downloaded here:  

[![Download Latest](https://forthebadge.com/api/badges/generate?panels=2&primaryLabel=Download&secondaryLabel=Latest&primaryBGColor=%231b3627&primaryTextColor=%23FFFFFF&secondaryBGColor=%2325bb2c&secondaryTextColor=%23FFFFFF&primaryFontSize=14&primaryFontWeight=600&primaryLetterSpacing=2&primaryFontFamily=Roboto&primaryTextTransform=uppercase&secondaryFontSize=14&secondaryFontWeight=700&secondaryLetterSpacing=2&secondaryFontFamily=Roboto&secondaryTextTransform=uppercase&scale=1.2&borderRadius=5&primaryTextShadowBlur=3&secondaryTextShadowColor=%23000000)](https://github.com/space928/QPlayer/releases/latest)

The latest pre-release of QPlayer can be downloaded here:  

[![Download Prerelease](https://forthebadge.com/api/badges/generate?panels=2&primaryLabel=Download&secondaryLabel=Prerelease&primaryBGColor=%231b3627&primaryTextColor=%23FFFFFF&secondaryBGColor=%23bb8c25&secondaryTextColor=%23FFFFFF&primaryFontSize=14&primaryFontWeight=600&primaryLetterSpacing=2&primaryFontFamily=Roboto&primaryTextTransform=uppercase&secondaryFontSize=14&secondaryFontWeight=700&secondaryLetterSpacing=2&secondaryFontFamily=Roboto&secondaryTextTransform=uppercase&scale=1.2&borderRadius=5&primaryTextShadowBlur=3&secondaryTextShadowColor=%23000000)](https://github.com/space928/QPlayer/releases/tag/latest)

You have a choice to download either `QPlayer-release.zip` or `QPlayer-release-sc.zip`, the `-sc.zip` version includes the entire dotnet 
runtime, should you need this, hence why it's so much bigger. For most users, the regular version is recommended.

To run, simply extract the `.zip` file and run `QPlayer.exe`

## Building
QPlayer can be built with Visual Studio 2026 using the .NET SDK 10.

Only Windows is officially supported for now.

## AI Policy
We live in a time of rapid technological development, and large language models seem to be the current bubble. As a software 
engineer I find the technology fascinating, as an artist and craftsperson, I find it worrying. Fundamentally, I take issue
with the way LLMs are trained (off of the hard work and un-licensed intellectual property of others), how they are often 
used (as a means to skirt licensing requirements of open source projects, by 're-writing' them), and the ethical and 
environmental issues that surround this. 

Hence, my wish is that LLMs not be used to generate derivative or transformative works based on QPlayer, further, contributions 
consisting largely of LLM generated content will not be accepted at my discretion. Consider these stipulations to be additional
requirements to those in the LICENSE.md file. That being said, I'm not your mother, and what you do in private is none of my 
concern, I only take issue when you publish works which break these requirements. <3

Further to the stipulations of the GPLv3 license, QPlayer, it's source code, and any derivative projects of this code, may not 
be used to train large language models.
