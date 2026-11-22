<<<<<<< HEAD
# FlowDesk

FlowDesk is a lightweight, customizable command palette for Windows, designed to bring everyday tools, applications, and productivity features into one convenient interface.

Instead of navigating through multiple applications or searching through Windows menus, FlowDesk provides a centralized place to execute commands, access frequently used tools, and simplify repetitive tasks.

The goal is to create a fast, minimalistic, and practical desktop utility that makes working with Windows more convenient.

## Features

FlowDesk aims to provide a collection of tools accessible through a simple command-based interface.

### Application Launcher
- Launch applications directly using commands.
- Quickly access frequently used programs.
- Open files and directories.
- Search for installed applications.

### Productivity Tools
- Create and manage quick notes.
- Start timers and focus sessions.
- Access clipboard history.

### Customization
- Minimalistic interface.
- Customizable commands and shortcuts.
- Configurable appearance.
- Designed to remain lightweight and unobtrusive.

##  Philosophy

FlowDesk is built around three main principles:

- **Simplicity:** Keep the interface clean and easy to understand.
- **Efficiency:** Make frequently used actions accessible with minimal effort.
- **Flexibility:** Allow users to customize their workflow and extend functionality.

Rather than becoming another resource-heavy productivity application, FlowDesk aims to provide a collection of small, useful tools that work together seamlessly.

## Technologies

- **Language:** C#
- **Framework:** .NET
- **UI Framework:** Windows Presentation Foundation (WPF)
- **Platform:** Windows

##  Development Status

FlowDesk is currently under development.

Features, functionality, and the user interface may change as development progresses.

Contributions, suggestions, and bug reports are welcome!

##  License

This project is licensed under the MIT License. See the `LICENSE` file for details.

---

*FlowDesk — Your desktop, simplified.*
=======
# FlowDeck

FlowDeck is a Windows desktop launcher and command palette.

**Publisher:** snurrvix

Features include application discovery, command shortcuts, timers, notes, themes, and configurable in-memory clipboard history.

## Build

Requires the .NET 9 SDK and Windows:

powershell
dotnet build FlowDeck.csproj --configuration Release


## Publish

The folder profile creates a self-contained, single-file `win-x64` build in `publish\`:

powershell
dotnet publish FlowDeck.csproj --property:PublishProfile=FolderProfile


Clipboard history is kept in memory and can be disabled or cleared in Settings. Clipboard opt-out markers and configured application exclusions are honored.
>>>>>>> 20d7182 (Initial FlowDeck source)
