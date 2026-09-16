# TrainerScheduler

A Windows desktop application for building and managing academic timetables for departments, faculty members, rooms, courses, and students.

## Highlights
- WPF desktop UI on .NET 8
- Faculty, department, student, and master timetable views
- Course/room/faculty scheduling constraints
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

The Release configuration treats compiler warnings as errors.

## Data and privacy
This public source package contains synthetic sample data only. It does not include a production database, real trainer/student records, local Visual Studio user files, backup source files, or organization-specific branding.

## Language
The user interface and public source comments/documentation are English-only. Legacy Arabic CSV/import compatibility is retained in code through Unicode escape sequences so Arabic characters do not appear directly in the public source tree.

## Security notes
- Passwords are stored using salted PBKDF2 hashes.
- LAN synchronization currently uses HTTP and is intended only for trusted local networks. Do not expose the LAN service directly to the public Internet without adding TLS and appropriate network controls.
- The public repository does not contain a production licensing secret. It falls back to a clearly marked demo value; private/commercial builds should provide `TRAINER_SCHEDULER_LICENSE_SECRET` through a secure deployment process and keep any serial-generation tooling outside the client repository.

## Repository hygiene
The public package excludes Visual Studio user files, backups, build output, local databases, logs, publish output, and private license files through `.gitignore`.

## Developer
**Nash Alj**  
Electrical Engineer / Software Developer  
`n.alhejaili1@gmail.com`

## Status
This is a portfolio/public-source version of an actively developed scheduling application. Before distributing a production build, run the Release checklist in `Docs/ReleaseChecklist.md` on Windows.
