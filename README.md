# S7Packer
A simple command line application that unpacks and packs archive files (*.bba) of the game "The Settlers 7 - Paths to a Kingdom" and its History Edition.  

**This is a full rewrite of [this Japanese tool here](https://wikiwiki.jp/settlers7/SettlersArchiver%E3%81%AE%E4%BD%BF%E3%81%84%E6%96%B9) originally created by kitune.**

## Usage
Simply launch the executable with the path to the .bba file and optionally the path to the packing directory as the given command line arguments.  

For example:
```
C:\Settlers\S7Packer.exe C:\Settlers\Maps.bba -- Unpacks the content of the archive file Maps.bba into a folder called Maps_Extracted
C:\Settlers\S7Packer.exe C:\Settlers\Maps.bba C:\Settlers\Maps_Extracted\ -- Packs the contents of Maps_Extracted into Maps.bba
```

## Features
- Extracts all data from Settlers 7 .bba archive files from both the Demo and the Final Release of the game.
- Can repack those files into a .bba archive file (Important: Only modifications of files are possible).
- Works on both Windows and Linux, fast file encryption/decryption by utilizing `Span<T>`.
- Fixes some errors (like repacking LngEN.bba resulting in corruption and data loss) of the old tool.

**Should there be any questions: [Settlers Discord Server](https://discord.gg/7SGkQtAAET).**
