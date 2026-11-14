# S7Packer
A simple command line application that unpacks and packs archive files (*.bba) of the game "The Settlers 7 - Paths to a Kingdom" and its History Edition.  

**This is a full rewrite of [this Japanese tool here](https://wikiwiki.jp/settlers7/SettlersArchiver%E3%81%AE%E4%BD%BF%E3%81%84%E6%96%B9) originally created by kitune.**

## Usage
Simply launch the executable with the path to the .bba file and optionally the path to the packing directory as the given command line arguments.  

For example:
```
C:\Settlers\S7Packer.exe C:\Settlers\Maps.bba -- Unpacks the content of the archive file Maps.bba into a folder called Maps_Extracted
C:\Settlers\S7Packer.exe C:\Settlers\Maps_Extracted\ C:\Settlers\Maps.bba -- Packs the contents of Maps_Extracted into Maps.bba
```

To use the demo, pass the command line argument `--demo` to the S7Packer.

## Features
- Extracts all data from Settlers 7 .bba archive files from both the demo and the final release of the game.
- Can repack files into a .bba archive file. In contrast to the original tool, it is also possible to add and remove files from an archive.
- Supports both Windows and Linux.
- Fixes multiple errors (like extracting Sound.bba or rebuilding the HashTable) of the original tool.

**Should there be any questions: [Settlers Discord Server](https://discord.gg/7SGkQtAAET).**
