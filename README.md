FlowDesk — Your desktop, simplified.

FlowDesk

FlowDesk is a lightweight Windows desktop launcher and command palette designed to simplify everyday tasks and improve workflow efficiency.

Publisher: snurrvix

Features include:

* Application discovery and launching.
* Custom command shortcuts.
* Built-in timers and notes.
* Customizable themes.
* Configurable in-memory clipboard history.

Build

Requires the .NET 9 SDK and Windows.

dotnet build FlowDesk.csproj --configuration Release

Publish

The folder profile creates a self-contained, single-file win-x64 build in the publish\ directory.

dotnet publish FlowDesk.csproj --property:PublishProfile=FolderProfile