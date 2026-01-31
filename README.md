# S7Packer
A simple command line application that unpacks and packs archive files (*.bba) of the game "The Settlers 7 - Paths to a Kingdom" and the History Edition.  
**This is a full rewrite of [this Japanese tool here](https://wikiwiki.jp/settlers7/SettlersArchiver%E3%81%AE%E4%BD%BF%E3%81%84%E6%96%B9) originally created by kitune.**

## Usage
Simply launch the executable with the path to the .bba file and optionally the path to the packing directory as its command line arguments.
For example: S7Packer.exe C:\\Settlers\\Maps.bba C:\\Settlers\\PackingFolder\\ -- Packs the content of PackingFolder into Maps.bba

## Features
- Can extract all data from Settlers 7 .bba archive files from both the Demo and the final release of the game.
- Can repack those files into a .bba archive file (Important: Only modifications to files are possible, it is currently not possible to add/remove files).
- Works on both Windows and Linux, fast file handling by utilizing Span<T>.
- Fixes some errors (like LngEN.bba) which were previously corrupted by the old tool.

**Should you have any questions: [Settlers Discord Server](https://discord.gg/7SGkQtAAET).**