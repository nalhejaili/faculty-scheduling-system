# Faculty Scheduling System

A Windows desktop application for building and managing academic timetables for departments, faculty members, rooms, courses, and students.

## Highlights
- WPF desktop UI on .NET 8
- Faculty, department, student, and master timetable views
- Course, room, and faculty scheduling constraints
- Manual assignments and scheduling overrides
- Student plans and enrollment workflows
- CSV and JSON import/export
- SQLite persistence with Entity Framework Core
- Role-based local authentication
- Optional LAN synchronization for trusted local networks
- Printable timetable views and operational diagnostics

## Tech stack
- C# / .NET 8
- WPF
- Entity Framework Core 8
- SQLite
- ASP.NET Core shared framework for the optional LAN service

## Naming compatibility note
This project was originally developed for scheduling **vocational trainers**, so some legacy internal identifiers still use names such as `TrainerScheduler` and `MiniTrainerScheduler`.

The public-facing terminology was later updated to **Faculty Member / Faculty Members** as the application evolved toward a broader academic scheduling use case. The legacy internal identifiers are intentionally preserved to avoid unnecessary changes to established namespaces, project references, and application wiring.

This naming difference does **not** affect the application's scheduling logic, data model, or runtime functionality.

## Build
Requirements:
- Windows
- .NET 8 SDK
- Visual Studio 2022 or another Windows-capable .NET/WPF toolchain

```text
Open MiniTrainerScheduler.sln
Select Release / Any CPU
Rebuild Solution
```

The Release configuration treats compiler warnings as errors. The published source was successfully built in Release configuration on Windows before publication with 0 warnings and 0 errors.

## First run
The public portfolio build starts directly in **Free Demo** mode and does not require a license serial.

If the local database does not contain any users, the application opens **Create First Administrator**. The reviewer chooses the administrator username and password during first-run setup; no fixed administrator password is embedded in the public source.

## Data and privacy
This public source package contains synthetic sample data only. It does not include a production database, real faculty/student records, local Visual Studio user files, backup source files, or organization-specific branding.

## Language
The user interface and public source comments/documentation are English-only. Legacy Arabic CSV/import compatibility is retained in code through Unicode escape sequences so Arabic characters do not appear directly in the public source tree.

## Security notes
- Passwords are stored using salted PBKDF2 hashes.
- The public portfolio build runs in Free Demo mode and bypasses serial activation.
- The public repository does not contain the private license generator or a production licensing secret.
- Private/commercial deployments should keep licensing secrets and serial-generation tooling outside the client repository.
- LAN synchronization currently uses HTTP and is intended only for trusted local networks. Do not expose the LAN service directly to the public Internet without adding TLS and appropriate network controls.

## Repository hygiene
The public package excludes Visual Studio user files, backups, build output, local databases, logs, publish output, and private license files through `.gitignore`.

## Developer
**Nash Alj**  
Electrical Engineer / Software Developer  
`n.alhejaili1@gmail.com`

## Status
This is a portfolio/public-source version of an actively developed scheduling application. Before distributing a production build, run the Release checklist in `Docs/ReleaseChecklist.md` on Windows.
