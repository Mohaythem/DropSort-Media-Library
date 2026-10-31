# TBD Movies (To Be Determined Movies)

A modern, fast, and local-first desktop media library for Windows, built with **.NET 10** and **WinUI 3 (Windows App SDK)**.

> **TBD** stands for **To Be Determined Movies** — your personal movie and TV show catalog with complete local privacy, Trakt synchronization, and safety-first file management.

---

## ✨ Features

- **🎬 Movies & TV Shows Management**: Organize your local video collection with rich metadata, high-resolution backdrops, genres, ratings, and multi-season TV episode tracking.
- **🔍 Smart TMDB Matching**: Automatic filename parsing and matching against The Movie Database (TMDB) with interactive candidate selection.
- **🔄 Two-Way Trakt Synchronization**: Seamlessly sync watch history, ratings, watchlists, and custom playlists with Trakt.tv with automatic rate-limit backoff.
- **🛡️ Safety-First Architecture**: Read-only by default. Physical video files are never renamed, moved, or deleted without explicit user preview and confirmation.
- **🌍 Native Arabic & English Support (Full RTL/LTR)**: Flawless bidirectional layout switching with Fluent typography and custom localization.
- **⚡ Local-First & Private**: All your library data lives locally in SQLite (WAL mode). API keys and tokens are securely encrypted using Windows DPAPI and Credential Manager.
- **🎨 Modern Windows 11 UI**: Crafted with WinUI 3, Mica Alt materials, responsive poster grids, and sleek themes (Dark, Light, Slate).

---

## 🛠️ Tech Stack

- **UI Framework**: .NET 10 (C# 14) + WinUI 3 / Windows App SDK
- **Database**: SQLite (`Microsoft.Data.Sqlite`) with ACID transactions & WAL mode
- **Integrations**: TMDB API & Trakt.tv REST API
- **Security**: Windows Data Protection API (DPAPI) & Windows Credential Manager
- **Packaging**: Inno Setup Installer & Portable Self-Contained Win-x64

---

## 🚀 Download & Installation

Download the latest official release directly from **[GitHub Releases](https://github.com/Mohaythem/DropSort-Media-Library/releases)**:

- **Installer (`DropSortSetup.exe`)**: Automatic setup with Desktop and Start Menu shortcuts.
- **Portable Edition (`DropSort-win-x64.zip`)**: Ready to run anywhere without installation. Just extract and launch `DropSort.UI.exe`.

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)** — keeping it free and open-source for everyone. Any derivative work or modifications must also remain open-source under the same terms.
