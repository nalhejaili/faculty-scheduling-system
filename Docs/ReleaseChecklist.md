# Release Checklist

## Build
- Open `MiniTrainerScheduler.sln`.
- Select `Release` and `Any CPU`.
- Run **Clean Solution** and **Rebuild Solution**.
- Release builds treat warnings as errors.

## Smoke test
- Start the application and sign in.
- Open Department, Faculty, Student, and Master timetable views.
- Generate a timetable.
- Verify save/load flows used by the release.
- Verify LAN synchronization when enabled.
- Open the About window and verify public developer information.

## Packaging
- Publish using the current Visual Studio/.NET publish profile.
- If an installer is used, build it from the release output.

## Final sanity check
- Install on a clean Windows machine or VM.
- Launch the application.
- Confirm the local database is created under `%LocalAppData%\TrainerScheduler`.
- Confirm no local developer paths, credentials, or private data are packaged.
