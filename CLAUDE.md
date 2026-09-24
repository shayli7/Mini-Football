# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

The whole guide lives in `AGENTS.md`, which other AI coding tools also read. It is imported here, so
it loads with this file. Add new guidance there, not here, so the two cannot drift apart.

@AGENTS.md

## UI work

Anything under `Assets/Scripts/UI/` also follows the UI style guide: palette and theme tokens,
button variants, motion rules and the screen-building pattern. Read it before changing any screen:
`Assets/Scripts/UI/AGENTS.md`.

## Cloud sessions

A claude.ai cloud container has no Unity install, and its network policy blocks downloading a .NET
SDK. So the compile check in `AGENTS.md` cannot run there. Say so when handing work back, and ask the
user to compile in Unity (or run the compile check on their Windows machine) before relying on it.
