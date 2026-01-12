# Changelog

All notable changes to DaVinci Time Tracker will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Nothing yet

### Changed
- Nothing yet

### Fixed
- Nothing yet

## [1.0.0] - 2026-01-12

### Added
- Initial release
- Automatic DaVinci Resolve project detection and time tracking
- Smart tracking with grace periods (3 min start, 10 min end)
- Web dashboard at http://localhost:5555
- Per-project time statistics
- System tray integration with status display
- Auto-start with Windows option
- View logs from tray menu
- Open logs folder from tray menu
- Project deletion with confirmation
- Automatic Python path detection
- Support for Python 3.8+ in multiple installation locations
- User data stored in `%LOCALAPPDATA%\DaVinciTimeTracker\`
- SQLite database with automatic migrations
- Crash recovery for unclosed sessions
- Inactivity detection (1 minute threshold)
- Focus tracking (only tracks when DaVinci is active)

### Technical
- Built with .NET 9 and ASP.NET Core
- Entity Framework Core with SQLite
- Serilog for structured logging
- Windows Forms for tray icon
- Python interop for DaVinci Resolve API
- Centralized path management (AppPaths)
- Debug mode with shorter grace periods (3s/5s)

### Requirements
- Windows 10 or later
- DaVinci Resolve Studio (API access)
- Python 3.8 or later
- .NET 9 Desktop Runtime

---

## How to Update This File

Before each release, update the `[Unreleased]` section:

### Template for new version:

```markdown
## [X.Y.Z] - YYYY-MM-DD

### Added
- New features go here

### Changed
- Changes to existing functionality

### Deprecated
- Soon-to-be removed features

### Removed
- Removed features

### Fixed
- Bug fixes

### Security
- Security fixes
```

### Version Numbering Guide

- **Major (X.0.0)**: Breaking changes, major new features
- **Minor (0.X.0)**: New features, backwards compatible
- **Patch (0.0.X)**: Bug fixes, minor improvements

### Examples

```markdown
## [1.1.0] - 2026-02-01

### Added
- Export time tracking data to CSV
- Weekly/monthly reports in dashboard

### Fixed
- Grace period calculation bug when system sleeps
- Memory leak in activity monitor
```
