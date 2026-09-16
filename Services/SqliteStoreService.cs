using Microsoft.EntityFrameworkCore;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrainerScheduler.Data;
using TrainerScheduler.Data.Entities;
using TrainerScheduler.Security;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// - Uses EnsureCreated (migrations can be introduced later).
    /// - Designed to be safe even if DB is empty: simply returns without changing Store.
    /// </summary>
    public sealed class SqliteStoreService
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<AppDbContext> _options;

        public SqliteStoreService()
        {
            _dbPath = GetDefaultDbPath();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
        }

        public string DatabasePath => _dbPath;

        public async Task InitializeAsync()
        {
            // Apply a pending restore (requested from UI) before opening any SQLite connections.
            // This avoids file-lock issues while the app is running.
            try
            {
                var pendingFlag = _dbPath + ".restore.pending";
                var stagedRestore = _dbPath + ".restore.db";

                if (File.Exists(pendingFlag) && File.Exists(stagedRestore))
                {
                    // Keep a safety copy of current DB (if any).
                    if (File.Exists(_dbPath))
                    {
                        var safety = _dbPath + $".pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db";
                        File.Copy(_dbPath, safety, overwrite: true);
                    }

                    File.Copy(stagedRestore, _dbPath, overwrite: true);

                    try { File.Delete(stagedRestore); } catch { /* ignore */ }
                    try { File.Delete(pendingFlag); } catch { /* ignore */ }
                }
            }
            catch
            {
                // Ignore restore errors to avoid blocking app startup.
                // If restore fails, the user can try again.
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

            await using var db = new AppDbContext(_options);
            await db.Database.EnsureCreatedAsync();

            // Ensure auth table exists even when the DB was created in older versions
            // (EnsureCreated will NOT add new tables to existing DBs).
            await EnsureUsersTableAsync(db);

            // even when the DB was created in older versions.
            await EnsureAssignmentsTableAsync(db);

            await EnsureFacultySlotBlocksTableAsync(db);


            await EnsureAssignmentsSchemaUpgradesAsync(db);
            await EnsureFacultySlotBlocksSchemaUpgradesAsync(db);
            await NormalizeLegacyNullsAsync(db);

            await EnsureServerMetaTableAsync(db);

            await EnsureStudentsTableAsync(db);
            await EnsureStudentPlansTableAsync(db);
            await EnsureStudentEnrollmentsTableAsync(db);

            await EnsureStudentUnassignedTableAsync(db);

            // Practical flags + course hour splits (persisted from JSON import)
            await EnsurePracticalCoursesTableAsync(db);
            await EnsureCourseHourSplitsTableAsync(db);


            await EnsureCourseFacultyOverridesTableAsync(db);

            await EnsureCourseCodeFacultyOverridesTableAsync(db);

            await EnsureCourseCodeMapsTableAsync(db);

            // Ensure the Courses table has the CourseCode column, and create an index for fast lookups.
            await TryAddColumnAsync(db, "Courses", "CourseCode TEXT NULL");
            try
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Courses_CourseCode ON Courses(CourseCode);");
            }
            catch
            {
                // ignore (older SQLite versions / transient issues)
            }
        }

        private static async Task EnsureCourseCodeMapsTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS CourseCodeMaps (
    CourseCode TEXT PRIMARY KEY,
    CourseId INTEGER NULL,
    CourseName TEXT NULL,
    Notes TEXT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_CourseCodeMaps_CourseId ON CourseCodeMaps(CourseId);
";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureStudentsTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS Students (
    Id INTEGER PRIMARY KEY,
    StudentNo TEXT NULL,
    FullName TEXT NOT NULL,
    DepartmentId INTEGER NULL,
    Level INTEGER NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Students_StudentNo ON Students(StudentNo);
CREATE INDEX IF NOT EXISTS IX_Students_DepartmentId ON Students(DepartmentId);
";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureStudentPlansTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS StudentPlans (
    Id INTEGER PRIMARY KEY,
    StudentId INTEGER NOT NULL,
    CourseId INTEGER NOT NULL,
    Priority INTEGER NOT NULL DEFAULT 0,
    IsRepeat INTEGER NOT NULL DEFAULT 0,
    TermKey TEXT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StudentPlans_StudentId ON StudentPlans(StudentId);
CREATE INDEX IF NOT EXISTS IX_StudentPlans_CourseId ON StudentPlans(CourseId);
";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureStudentEnrollmentsTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS StudentEnrollments (
    Id INTEGER PRIMARY KEY,
    StudentId INTEGER NOT NULL,
    AssignmentId INTEGER NOT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StudentEnrollments_StudentId ON StudentEnrollments(StudentId);
CREATE INDEX IF NOT EXISTS IX_StudentEnrollments_AssignmentId ON StudentEnrollments(AssignmentId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_StudentEnrollments_StudentId_AssignmentId ON StudentEnrollments(StudentId, AssignmentId);
";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureStudentUnassignedTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS StudentUnassigned (
    Id INTEGER PRIMARY KEY,
    StudentId INTEGER NOT NULL,
    CourseId INTEGER NOT NULL,
    Kind TEXT NOT NULL DEFAULT '',
    Reason TEXT NOT NULL DEFAULT '',
    Details TEXT NULL,
    SavedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StudentUnassigned_StudentId ON StudentUnassigned(StudentId);
CREATE INDEX IF NOT EXISTS IX_StudentUnassigned_CourseId ON StudentUnassigned(CourseId);
CREATE INDEX IF NOT EXISTS IX_StudentUnassigned_Reason ON StudentUnassigned(Reason);
";

            await db.Database.ExecuteSqlRawAsync(sql);

            // Legacy upgrades (safe no-ops if columns already exist)
            await TryAddColumnAsync(db, "StudentUnassigned", "Kind TEXT NOT NULL DEFAULT ''");
            await TryAddColumnAsync(db, "StudentUnassigned", "Reason TEXT NOT NULL DEFAULT ''");
            await TryAddColumnAsync(db, "StudentUnassigned", "Details TEXT NULL");
            await TryAddColumnAsync(db, "StudentUnassigned", "SavedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)");
        }

        // -------------------------
        // -------------------------

        private static async Task EnsurePracticalCoursesTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS PracticalCourses (
    CourseId INTEGER NOT NULL PRIMARY KEY
);
";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureCourseHourSplitsTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS CourseHourSplits (
    CourseId INTEGER NOT NULL PRIMARY KEY,
    TheoryHours REAL NOT NULL DEFAULT 0,
    PracticalHours REAL NOT NULL DEFAULT 0,
    PreferSameInstructor INTEGER NOT NULL DEFAULT 1,
    UpdatedUtc TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_CourseHourSplits_CourseId ON CourseHourSplits(CourseId);
";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        

        private static async Task EnsureCourseFacultyOverridesTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS CourseFacultyOverrides (
    CourseId INTEGER NOT NULL,
    FacultyId INTEGER NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    PRIMARY KEY (CourseId, FacultyId)
);
CREATE INDEX IF NOT EXISTS IX_CourseFacultyOverrides_CourseId ON CourseFacultyOverrides(CourseId);
CREATE INDEX IF NOT EXISTS IX_CourseFacultyOverrides_FacultyId ON CourseFacultyOverrides(FacultyId);
";
            await db.Database.ExecuteSqlRawAsync(sql);
        
            await TryAddColumnAsync(db, "CourseFacultyOverrides", "CourseCode TEXT NULL");
            try
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_CourseFacultyOverrides_CourseCode ON CourseFacultyOverrides(CourseCode);");
            }
            catch
            {
                // ignore
            }
        }


        private static async Task EnsureCourseCodeFacultyOverridesTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS CourseCodeFacultyOverrides (
    ScopeDepartmentId INTEGER NOT NULL,
    CourseCode TEXT NOT NULL,
    FacultyId INTEGER NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    PRIMARY KEY (ScopeDepartmentId, CourseCode, FacultyId)
);
CREATE INDEX IF NOT EXISTS IX_CourseCodeFacultyOverrides_ScopeDepartmentId ON CourseCodeFacultyOverrides(ScopeDepartmentId);
CREATE INDEX IF NOT EXISTS IX_CourseCodeFacultyOverrides_CourseCode ON CourseCodeFacultyOverrides(CourseCode);
CREATE INDEX IF NOT EXISTS IX_CourseCodeFacultyOverrides_FacultyId ON CourseCodeFacultyOverrides(FacultyId);
";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

private static async Task EnsureUsersTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    Role TEXT NOT NULL,
    DepartmentId INTEGER NULL,
    CreatedUtc TEXT NOT NULL
);";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureAssignmentsTableAsync(AppDbContext db)
        {
            // NOTE: We intentionally keep the schema simple and free of FK constraints
            // to avoid breaking older/partial datasets.
            const string sql = @"
CREATE TABLE IF NOT EXISTS Assignments (
    Id INTEGER PRIMARY KEY,
    DepartmentId INTEGER NOT NULL,
    CourseId INTEGER NOT NULL,
    SectionIndex INTEGER NOT NULL,
    SlotId INTEGER NOT NULL,
    FacultyId INTEGER NOT NULL,
    RoomId INTEGER NULL,
    Status TEXT NOT NULL,
    Kind TEXT NOT NULL,
    SavedUtc TEXT NOT NULL
);";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureFacultySlotBlocksTableAsync(AppDbContext db)
        {
            // Simple schema, no FK constraints.
            const string sql = @"
CREATE TABLE IF NOT EXISTS FacultySlotBlocks (
    Id INTEGER PRIMARY KEY,
    FacultyId INTEGER NOT NULL,
    SlotId INTEGER NOT NULL,
    Reason TEXT NULL,
    SavedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_FacultySlotBlocks_FacultyId ON FacultySlotBlocks(FacultyId);
CREATE INDEX IF NOT EXISTS IX_FacultySlotBlocks_SlotId ON FacultySlotBlocks(SlotId);
";

            await db.Database.ExecuteSqlRawAsync(sql);
        }

        // -------------------------
        // -------------------------

        
// -------------------------
// -------------------------

private static async Task EnsureAssignmentsSchemaUpgradesAsync(AppDbContext db)
{
    // Add missing columns in older DBs.
    await TryAddColumnAsync(db, "Assignments", "DepartmentId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "Assignments", "CourseId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "Assignments", "SectionIndex INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "Assignments", "SlotId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "Assignments", "FacultyId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "Assignments", "RoomId INTEGER NULL");
    await TryAddColumnAsync(db, "Assignments", "Status TEXT NOT NULL DEFAULT ''");
    await TryAddColumnAsync(db, "Assignments", "Kind TEXT NOT NULL DEFAULT ''");
    // Use CURRENT_TIMESTAMP so SQLite provides a parsable value for existing rows.
    await TryAddColumnAsync(db, "Assignments", "SavedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)");
}

private static async Task EnsureFacultySlotBlocksSchemaUpgradesAsync(AppDbContext db)
{
    await TryAddColumnAsync(db, "FacultySlotBlocks", "FacultyId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FacultySlotBlocks", "SlotId INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FacultySlotBlocks", "Reason TEXT NULL");
    await TryAddColumnAsync(db, "FacultySlotBlocks", "SavedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)");
}

private static async Task NormalizeLegacyNullsAsync(AppDbContext db)
{
    // Older DBs might have NULLs in text/date columns. Normalize to safe defaults.
    try
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE Assignments SET Status = '' WHERE Status IS NULL;");
    }
    catch { /* ignore if column doesn't exist */ }

    try
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE Assignments SET Kind = '' WHERE Kind IS NULL;");
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

    try
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE Assignments SET SavedUtc = CURRENT_TIMESTAMP WHERE SavedUtc IS NULL OR SavedUtc = '';");
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

    try
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE FacultySlotBlocks SET Reason = NULL WHERE Reason = '';");
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

    try
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE FacultySlotBlocks SET SavedUtc = CURRENT_TIMESTAMP WHERE SavedUtc IS NULL OR SavedUtc = '';");
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }
}

private static async Task TryAddColumnAsync(AppDbContext db, string tableName, string columnDef)
{
    // NOTE: This helper is only used for schema upgrades with *internal* constants.
    // We still validate identifiers to avoid any accidental SQL injection surface.
    if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnDef))
        throw new ArgumentException("Invalid tableName/columnDef.");

    // columnDef is expected to be: "<ColumnName> <SQL TYPE/CONSTRAINTS...>"
    var firstSpace = columnDef.IndexOf(' ');
    if (firstSpace <= 0)
        throw new ArgumentException("columnDef must start with a column name followed by a space.", nameof(columnDef));

    var columnName = columnDef[..firstSpace].Trim();
    var columnRest = columnDef[firstSpace..].Trim();

    if (!IsSafeSqliteIdentifier(tableName) || !IsSafeSqliteIdentifier(columnName))
        throw new InvalidOperationException("Unsafe SQLite identifier.");

    // Basic guard: never allow statement terminators/comments in the definition.
    // (The remainder is controlled by code, but this prevents future accidental injection.)
    if (columnRest.Contains(';') || columnRest.Contains("--") || columnRest.Contains("/*") || columnRest.Contains("*/"))
        throw new InvalidOperationException("Unsafe column definition.");

    var sql = "ALTER TABLE " + QuoteSqliteIdentifier(tableName) +
              " ADD COLUMN " + QuoteSqliteIdentifier(columnName) +
              " " + columnRest + ";";

    try
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    catch (Exception ex)
    {
        // SQLite throws on duplicate column names. Ignore that specific case.
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            return;

        // Also ignore if the table doesn't exist yet (shouldn't happen because Ensure*Table runs first).
        if (msg.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            return;

        throw;
    }
}

private static bool IsSafeSqliteIdentifier(string name)
{
    // Allow only simple identifiers (letters, digits, underscore) and must not start with a digit.
    // This matches all identifiers used in this project (e.g., Assignments, SavedUtc, CourseCode).
    if (string.IsNullOrWhiteSpace(name)) return false;

    for (int i = 0; i < name.Length; i++)
    {
        var ch = name[i];
        var ok = (ch >= 'A' && ch <= 'Z') ||
                 (ch >= 'a' && ch <= 'z') ||
                 (ch >= '0' && ch <= '9') ||
                 ch == '_';

        if (!ok) return false;
        if (i == 0 && (ch >= '0' && ch <= '9')) return false;
    }

    return true;
}

private static string QuoteSqliteIdentifier(string name)
{
    // Quote identifiers with double quotes to preserve casing and avoid keyword collisions.
    // Since we validate allowed characters, no escaping is necessary.
    // IMPORTANT: Build the quoted identifier at runtime.
    // (A previous attempt used a malformed string literal that produced SQL text containing '+' tokens,
    //  leading to: SQLite Error 1: near "+": syntax error.)
    return "\"" + name + "\"";
}

private static async Task EnsureServerMetaTableAsync(AppDbContext db)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS ServerMeta (
    Id INTEGER PRIMARY KEY,
    Revision INTEGER NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
INSERT OR IGNORE INTO ServerMeta (Id, Revision, UpdatedUtc) VALUES (1, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
";
            await db.Database.ExecuteSqlRawAsync(sql);
        }


        // -------------------------
        // -------------------------

        /// <summary>
        /// Saves the currently generated assignments into SQLite.
        /// If scopeDepartmentId is provided (Supervisor), only that department's assignments are replaced.
        /// Admin saves the full snapshot.
        /// </summary>
        public async Task<(bool ok, string errorMessage)> SaveAssignmentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var now = DateTime.UtcNow;

                var src = store.Assignments.AsEnumerable();
                if (scopeDepartmentId is not null)
                    src = src.Where(a => a.DepartmentId == scopeDepartmentId.Value);

                var rows = src.Select(a => new AssignmentEntity
                {
                    DepartmentId = a.DepartmentId,
                    CourseId = a.CourseId,
                    SectionIndex = a.SectionIndex,
                    SlotId = a.SlotId,
                    FacultyId = a.FacultyId,
                    RoomId = a.RoomId,
                    Status = a.Status ?? string.Empty,
                    Kind = a.Kind ?? string.Empty,
                    SavedUtc = now
                }).ToList();

                if (rows.Count == 0)
                    return (false, "There are no results to save.");

                // ------------------------------
                // 4B-5a: Replace strategy MUST be scoped by (DepartmentId + Level)
                // so saving Level-2 results does not wipe Level-1 for the same department.
                //
                // We infer the affected levels from CourseId -> Course.Level (from Store).
                // ------------------------------

                // Build affected dept->levels map (using Store.Courses as the source of truth)
                var affected = new Dictionary<int, HashSet<int>>();
                foreach (var r in rows)
                {
                    var c = store.Courses.FirstOrDefault(x => x.Id == r.CourseId);
                    if (c is null)
                        return (false, $"Unable to save the results because the course is unknown in the data store (CourseId={r.CourseId}).");

                    if (!affected.TryGetValue(r.DepartmentId, out var levels))
                    {
                        levels = new HashSet<int>();
                        affected[r.DepartmentId] = levels;
                    }
                    levels.Add(c.Level);
                }

                // Safety: if scoped, only keep that dept.
                if (scopeDepartmentId is int scopedDept)
                {
                    affected = affected
                        .Where(k => k.Key == scopedDept)
                        .ToDictionary(k => k.Key, k => k.Value);
                }

                await using var tx = await db.Database.BeginTransactionAsync();

                // Remove only the affected (dept + levels) slice.
                foreach (var kv in affected)
                {
                    var deptId = kv.Key;
                    var levels = kv.Value;

                    var courseIdsAtLevels = store.Courses
                        .Where(c => c.DepartmentId == deptId && levels.Contains(c.Level))
                        .Select(c => c.Id)
                        .ToList();

                    if (courseIdsAtLevels.Count == 0)
                        continue;

                    var toRemove = db.Assignments.Where(a => a.DepartmentId == deptId && courseIdsAtLevels.Contains(a.CourseId));
                    db.Assignments.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                db.Assignments.AddRange(rows);
                await db.SaveChangesAsync();

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Loads assignments from SQLite into Store.Assignments (replace in-memory list).
        /// If scopeDepartmentId is provided, only that department is loaded.
        /// Returns true when any assignments were loaded.
        /// </summary>
        public async Task<bool> TryLoadAssignmentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var q = db.Assignments.AsNoTracking().AsQueryable();
            if (scopeDepartmentId is not null)
                q = q.Where(a => a.DepartmentId == scopeDepartmentId.Value);
            var list = await q.OrderBy(a => a.DepartmentId)
                .ThenBy(a => a.CourseId)
                .ThenBy(a => a.SectionIndex)
                .ThenBy(a => a.SlotId)
                .ToListAsync();

            store.Assignments.Clear();
            foreach (var a in list)
            {
                store.Assignments.Add(new Assignment(
                    a.DepartmentId,
                    a.CourseId,
                    a.SectionIndex,
                    a.SlotId,
                    a.FacultyId,
                    a.RoomId,
                    a.Status ?? string.Empty,
                    a.Kind ?? string.Empty)
                {
                    Id = a.Id
                });
            }

            return list.Count > 0;
        }

        /// <summary>
        /// - Admin: replaces all blocks.
        /// - Supervisor: replaces only blocks for faculties that belong to his department.
        ///
        /// Unlike assignments, saving an empty list is considered valid (it means "no blocks").
        /// </summary>
        public async Task<(bool ok, string errorMessage)> SaveFacultySlotBlocksAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var now = DateTime.UtcNow;

                // Determine allowed faculties in scope
                HashSet<int>? allowedFacultyIds = null;
                if (scopeDepartmentId is not null)
                {
                    allowedFacultyIds = store.Faculties
                        .Where(f => f.DepartmentId == scopeDepartmentId.Value)
                        .Select(f => f.Id)
                        .ToHashSet();
                }

                // Build distinct rows
                var distinct = store.FacultySlotBlocks
                    .Where(b => allowedFacultyIds is null || allowedFacultyIds.Contains(b.FacultyId))
                    .GroupBy(b => new { b.FacultyId, b.SlotId })
                    .Select(g => g.First())
                    .ToList();

                await using var tx = await db.Database.BeginTransactionAsync();

                if (allowedFacultyIds is null)
                {
                    db.FacultySlotBlocks.RemoveRange(db.FacultySlotBlocks);
                }
                else
                {
                    var toRemove = db.FacultySlotBlocks.Where(x => allowedFacultyIds.Contains(x.FacultyId));
                    db.FacultySlotBlocks.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (distinct.Count > 0)
                {
                    var entities = distinct.Select(b => new FacultySlotBlockEntity
                    {
                        FacultyId = b.FacultyId,
                        SlotId = b.SlotId,
                        Reason = b.Reason,
                        SavedUtc = now
                    });

                    db.FacultySlotBlocks.AddRange(entities);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Loads faculty slot blocks from SQLite into Store.FacultySlotBlocks.
        /// Returns true when any blocks were loaded.
        /// </summary>
        public async Task<bool> TryLoadFacultySlotBlocksAsync(DataStore store)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var list = await db.FacultySlotBlocks.AsNoTracking()
                .OrderBy(b => b.FacultyId)
                .ThenBy(b => b.SlotId)
                .ToListAsync();

            store.FacultySlotBlocks.Clear();
            foreach (var b in list)
            {
                store.FacultySlotBlocks.Add(new FacultySlotBlock
                {
                    FacultyId = b.FacultyId,
                    SlotId = b.SlotId,
                    Reason = b.Reason ?? string.Empty
                });
            }

            return list.Count > 0;
        }

        // -------------------------
        // -------------------------

        public async Task<bool> TryLoadStudentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var q = db.Students.AsNoTracking().AsQueryable();
            if (scopeDepartmentId is not null)
                q = q.Where(s => s.DepartmentId == scopeDepartmentId.Value);

            var list = await q.OrderBy(s => s.Id).ToListAsync();

            store.Students.Clear();
            foreach (var s in list)
            {
                store.Students.Add(new Student
                {
                    Id = s.Id,
                    StudentNo = s.StudentNo ?? string.Empty,
                    FullName = s.FullName ?? string.Empty,
                    DepartmentId = s.DepartmentId,
                    Level = s.Level
                });
            }

            return list.Count > 0;
        }

        public async Task<(bool ok, string errorMessage)> SaveStudentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var now = DateTime.UtcNow;

                var src = store.Students.AsEnumerable();
                if (scopeDepartmentId is not null)
                    src = src.Where(s => s.DepartmentId == scopeDepartmentId.Value);

                var rows = src
                    .Where(s => s.Id > 0)
                    .Select(s => new StudentEntity
                    {
                        Id = s.Id,
                        StudentNo = string.IsNullOrWhiteSpace(s.StudentNo) ? null : s.StudentNo,
                        FullName = s.FullName ?? string.Empty,
                        DepartmentId = s.DepartmentId,
                        Level = s.Level,
                        CreatedUtc = now
                    })
                    .ToList();

                await using var tx = await db.Database.BeginTransactionAsync();

                if (scopeDepartmentId is null)
                {
                    db.Students.RemoveRange(db.Students);
                }
                else
                {
                    var toRemove = db.Students.Where(x => x.DepartmentId == scopeDepartmentId.Value);
                    db.Students.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (rows.Count > 0)
                {
                    db.Students.AddRange(rows);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<bool> TryLoadStudentPlansAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            // If supervisor: limit to students that belong to his department (using Students table).
            HashSet<int>? allowedStudentIds = null;
            if (scopeDepartmentId is not null)
            {
                allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                    .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                    .Select(s => s.Id)
                    .ToListAsync());
            }

            var q = db.StudentPlans.AsNoTracking().AsQueryable();
            if (allowedStudentIds is not null)
                q = q.Where(p => allowedStudentIds.Contains(p.StudentId));

            var list = await q.OrderBy(p => p.StudentId).ThenBy(p => p.CourseId).ToListAsync();

            store.StudentPlans.Clear();
            foreach (var p in list)
            {
                store.StudentPlans.Add(new StudentPlan
                {
                    Id = p.Id,
                    StudentId = p.StudentId,
                    CourseId = p.CourseId,
                    Priority = p.Priority,
                    IsRepeat = p.IsRepeat,
                    TermKey = AcademicTermKeyService.NormalizeForDisplay(p.TermKey),
                    CreatedUtc = p.CreatedUtc
                });
            }

            return list.Count > 0;
        }

        public async Task<(bool ok, string errorMessage)> SaveStudentPlansAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                HashSet<int>? allowedStudentIds = null;
                if (scopeDepartmentId is not null)
                {
                    // Use in-memory store if available; otherwise fallback to DB.
                    allowedStudentIds = store.Students
                        .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                        .Select(s => s.Id)
                        .Where(id => id > 0)
                        .ToHashSet();

                    if (allowedStudentIds.Count == 0)
                    {
                        allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                            .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                            .Select(s => s.Id)
                            .ToListAsync());
                    }
                }

                var src = store.StudentPlans.AsEnumerable();
                if (allowedStudentIds is not null)
                    src = src.Where(p => allowedStudentIds.Contains(p.StudentId));

                var rows = src.Select(p => new StudentPlanEntity
                {
                    // Id auto
                    StudentId = p.StudentId,
                    CourseId = p.CourseId,
                    Priority = p.Priority,
                    IsRepeat = p.IsRepeat,
                    TermKey = AcademicTermKeyService.NormalizeForStorage(p.TermKey),
                    CreatedUtc = p.CreatedUtc == default ? DateTime.UtcNow : p.CreatedUtc
                }).ToList();

                await using var tx = await db.Database.BeginTransactionAsync();

                if (allowedStudentIds is null)
                {
                    db.StudentPlans.RemoveRange(db.StudentPlans);
                }
                else
                {
                    var toRemove = db.StudentPlans.Where(x => allowedStudentIds.Contains(x.StudentId));
                    db.StudentPlans.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (rows.Count > 0)
                {
                    db.StudentPlans.AddRange(rows);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<bool> TryLoadStudentEnrollmentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            HashSet<int>? allowedStudentIds = null;
            if (scopeDepartmentId is not null)
            {
                allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                    .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                    .Select(s => s.Id)
                    .ToListAsync());
            }

            var q = db.StudentEnrollments.AsNoTracking().AsQueryable();
            if (allowedStudentIds is not null)
                q = q.Where(e => allowedStudentIds.Contains(e.StudentId));

            var list = await q.OrderBy(e => e.StudentId).ThenBy(e => e.AssignmentId).ToListAsync();

            store.StudentEnrollments.Clear();
            foreach (var e in list)
            {
                store.StudentEnrollments.Add(new StudentEnrollment
                {
                    Id = e.Id,
                    StudentId = e.StudentId,
                    AssignmentId = e.AssignmentId,
                    CreatedUtc = e.CreatedUtc
                });
            }

            return list.Count > 0;
        }

        public async Task<(bool ok, string errorMessage)> SaveStudentEnrollmentsAsync(DataStore store, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                HashSet<int>? allowedStudentIds = null;
                if (scopeDepartmentId is not null)
                {
                    allowedStudentIds = store.Students
                        .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                        .Select(s => s.Id)
                        .Where(id => id > 0)
                        .ToHashSet();

                    if (allowedStudentIds.Count == 0)
                    {
                        allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                            .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                            .Select(s => s.Id)
                            .ToListAsync());
                    }
                }

                var src = store.StudentEnrollments.AsEnumerable();
                if (allowedStudentIds is not null)
                    src = src.Where(e => allowedStudentIds.Contains(e.StudentId));

                var rows = src.Select(e => new StudentEnrollmentEntity
                {
                    StudentId = e.StudentId,
                    AssignmentId = e.AssignmentId,
                    CreatedUtc = e.CreatedUtc == default ? DateTime.UtcNow : e.CreatedUtc
                }).ToList();

                await using var tx = await db.Database.BeginTransactionAsync();

                if (allowedStudentIds is null)
                {
                    db.StudentEnrollments.RemoveRange(db.StudentEnrollments);
                }
                else
                {
                    var toRemove = db.StudentEnrollments.Where(x => allowedStudentIds.Contains(x.StudentId));
                    db.StudentEnrollments.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (rows.Count > 0)
                {
                    db.StudentEnrollments.AddRange(rows);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }


        // -------------------------
        // -------------------------

        public async Task<List<UnassignedPlan>> TryLoadStudentUnassignedPlansAsync(int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                HashSet<int>? allowedStudentIds = null;
                if (scopeDepartmentId is not null)
                {
                    allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                        .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                        .Select(s => s.Id)
                        .ToListAsync());
                }

                var q = db.StudentUnassigned.AsNoTracking().AsQueryable();
                if (allowedStudentIds is not null)
                    q = q.Where(x => allowedStudentIds.Contains(x.StudentId));

                var rows = await q.OrderBy(x => x.StudentId).ThenBy(x => x.CourseId).ThenBy(x => x.Id).ToListAsync();

                return rows.Select(x => new UnassignedPlan(
                        x.StudentId,
                        x.CourseId,
                        x.Kind ?? "",
                        x.Reason ?? "",
                        x.Details
                    ))
                    .ToList();
            }
            catch
            {
                return new List<UnassignedPlan>();
            }
        }

        public async Task<(bool ok, string errorMessage)> SaveStudentUnassignedPlansAsync(
            IReadOnlyCollection<UnassignedPlan> unassigned,
            int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                HashSet<int>? allowedStudentIds = null;
                if (scopeDepartmentId is not null)
                {
                    allowedStudentIds = new HashSet<int>(await db.Students.AsNoTracking()
                        .Where(s => s.DepartmentId == scopeDepartmentId.Value)
                        .Select(s => s.Id)
                        .ToListAsync());
                }

                var now = DateTime.UtcNow;

                var src = unassigned.AsEnumerable();
                if (allowedStudentIds is not null)
                    src = src.Where(u => allowedStudentIds.Contains(u.StudentId));

                var rows = src.Select(u => new StudentUnassignedEntity
                {
                    StudentId = u.StudentId,
                    CourseId = u.CourseId,
                    Kind = (u.Kind ?? "").Trim().ToUpperInvariant(),
                    Reason = u.Reason ?? "",
                    Details = string.IsNullOrWhiteSpace(u.Details) ? null : u.Details,
                    SavedUtc = now
                }).ToList();

                await using var tx = await db.Database.BeginTransactionAsync();

                if (allowedStudentIds is null)
                {
                    db.StudentUnassigned.RemoveRange(db.StudentUnassigned);
                }
                else
                {
                    var toRemove = db.StudentUnassigned.Where(x => allowedStudentIds.Contains(x.StudentId));
                    db.StudentUnassigned.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (rows.Count > 0)
                {
                    db.StudentUnassigned.AddRange(rows);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }


        // -------------------------
        // -------------------------

        public sealed record ServerRevision(long Revision, DateTime UpdatedUtc);

        public async Task<ServerRevision> GetServerRevisionAsync()
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var row = await db.ServerMeta.AsNoTracking().FirstAsync(x => x.Id == 1);
            return new ServerRevision(row.Revision, row.UpdatedUtc);
        }

        public async Task<ServerRevision> BumpServerRevisionAsync()
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var row = await db.ServerMeta.FirstAsync(x => x.Id == 1);
            row.Revision = row.Revision + 1;
            row.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return new ServerRevision(row.Revision, row.UpdatedUtc);
        }

        
// -------------------------
// -------------------------

public sealed record LegacyMaintenanceReport(
    DateTime RanUtc,
    bool AppliedFix,
    int AssignmentsTotal,
    int AssignmentsBadKey,
    int AssignmentsOrphanDepartment,
    int AssignmentsOrphanCourse,
    int AssignmentsOrphanSlot,
    int AssignmentsOrphanFaculty,
    int AssignmentsDeptMismatchCourse,
    int AssignmentsDeptRepaired,
    int AssignmentsDeleted,
    int BlocksTotal,
    int BlocksBadKey,
    int BlocksOrphanSlot,
    int BlocksOrphanFaculty,
    int BlocksDeleted,
    long? NewRevision)
{
    public string ToHumanText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"UTC timestamp: {RanUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Applied: {(AppliedFix ? "Yes" : "No (diagnostics only)")}");
        sb.AppendLine();
        sb.AppendLine("Assignments (Scheduling Results):");
        sb.AppendLine($"- Total: {AssignmentsTotal}");
        sb.AppendLine($"- Invalid keys: {AssignmentsBadKey}");
        sb.AppendLine($"- Missing references (department not found): {AssignmentsOrphanDepartment}");
        sb.AppendLine($"- Missing references (course not found): {AssignmentsOrphanCourse}");
        sb.AppendLine($"- Missing references (time slot not found): {AssignmentsOrphanSlot}");
        sb.AppendLine($"- Missing references (faculty member not found): {AssignmentsOrphanFaculty}");
        sb.AppendLine($"- Course department mismatches: {AssignmentsDeptMismatchCourse}");
        if (AppliedFix)
        {
            sb.AppendLine($"- Course department repaired: {AssignmentsDeptRepaired}");
            sb.AppendLine($"- Unrecoverable records deleted: {AssignmentsDeleted}");
        }
        sb.AppendLine();
        sb.AppendLine("Blocks (Faculty Availability/Assignments):");
        sb.AppendLine($"- Total: {BlocksTotal}");
        sb.AppendLine($"- Invalid keys: {BlocksBadKey}");
        sb.AppendLine($"- Missing references (time slot not found): {BlocksOrphanSlot}");
        sb.AppendLine($"- Missing references (faculty member not found): {BlocksOrphanFaculty}");
        if (AppliedFix)
            sb.AppendLine($"- Unrecoverable records deleted: {BlocksDeleted}");

        if (NewRevision is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"New revision: {NewRevision}");
        }

        return sb.ToString().Trim();
    }
}

public async Task<LegacyMaintenanceReport> DiagnoseLegacyAsync(int? scopeDepartmentId)
{
    var (ok, _, report) = await RunLegacyMaintenanceAsync(scopeDepartmentId, applyFix: false);
    return report;
}

public async Task<(bool ok, string errorMessage, LegacyMaintenanceReport report)> RunLegacyMaintenanceAsync(int? scopeDepartmentId, bool applyFix)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    try
    {
        // Load lookup IDs
        var deptIds = await db.Departments.AsNoTracking().Select(d => d.Id).ToListAsync();
        var facultyIds = await db.Faculties.AsNoTracking().Select(f => f.Id).ToListAsync();
        var slotIds = await db.Slots.AsNoTracking().Select(s => s.Id).ToListAsync();

        // Course -> Department map
        var courseDept = await db.Courses.AsNoTracking()
            .Select(c => new { c.Id, c.DepartmentId })
            .ToListAsync();

        var deptSet = deptIds.ToHashSet();
        var facultySet = facultyIds.ToHashSet();
        var slotSet = slotIds.ToHashSet();
        var courseSet = courseDept.Select(x => x.Id).ToHashSet();
        var courseDeptMap = courseDept.ToDictionary(x => x.Id, x => x.DepartmentId);

        // Query assignments in scope (if supervisor)
        var aq = db.Assignments.AsQueryable();
        if (scopeDepartmentId is not null)
            aq = aq.Where(a => a.DepartmentId == scopeDepartmentId.Value);

        var assignments = await aq.AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.DepartmentId,
                a.CourseId,
                a.SectionIndex,
                a.SlotId,
                a.FacultyId
            })
            .ToListAsync();

        int aTotal = assignments.Count;
        int aBadKey = 0, aOrDept = 0, aOrCourse = 0, aOrSlot = 0, aOrFac = 0, aMismatch = 0;

        foreach (var a in assignments)
        {
            if (a.DepartmentId <= 0 || a.CourseId <= 0 || a.SlotId <= 0 || a.FacultyId <= 0 || a.SectionIndex < 0)
                aBadKey++;

            if (!deptSet.Contains(a.DepartmentId)) aOrDept++;
            if (!courseSet.Contains(a.CourseId)) aOrCourse++;
            if (!slotSet.Contains(a.SlotId)) aOrSlot++;
            if (!facultySet.Contains(a.FacultyId)) aOrFac++;

            if (courseDeptMap.TryGetValue(a.CourseId, out var cd) && a.DepartmentId != cd)
                aMismatch++;
        }

        // Blocks in scope
        var bq = db.FacultySlotBlocks.AsQueryable();
        if (scopeDepartmentId is not null)
        {
            var deptId = scopeDepartmentId.Value;
            var allowedFacultyIds = await db.Faculties.AsNoTracking()
                .Where(f => f.DepartmentId == deptId)
                .Select(f => f.Id)
                .ToListAsync();

            var allowedSet = allowedFacultyIds.ToHashSet();
            bq = bq.Where(b => allowedSet.Contains(b.FacultyId));
        }

        var blocks = await bq.AsNoTracking()
            .Select(b => new { b.Id, b.FacultyId, b.SlotId })
            .ToListAsync();

        int bTotal = blocks.Count;
        int bBadKey = 0, bOrSlot = 0, bOrFac = 0;
        foreach (var b in blocks)
        {
            if (b.FacultyId <= 0 || b.SlotId <= 0)
                bBadKey++;
            if (!slotSet.Contains(b.SlotId)) bOrSlot++;
            if (!facultySet.Contains(b.FacultyId)) bOrFac++;
        }

        int aRepaired = 0, aDeleted = 0, bDeleted = 0;
        long? newRev = null;

        if (applyFix)
        {
            // 1) Repair department mismatch (canonicalize dept from course)
            var toFixIds = assignments
                .Where(a => courseDeptMap.TryGetValue(a.CourseId, out var cd) && a.DepartmentId != cd)
                .Select(a => a.Id)
                .ToList();

            if (toFixIds.Count > 0)
            {
                var fixRows = await db.Assignments.Where(x => toFixIds.Contains(x.Id)).ToListAsync();
                foreach (var row in fixRows)
                {
                    if (courseDeptMap.TryGetValue(row.CourseId, out var cd))
                    {
                        if (row.DepartmentId != cd)
                        {
                            row.DepartmentId = cd;
                            aRepaired++;
                        }
                    }
                }
                await db.SaveChangesAsync();
            }

            // Refresh sets? deptSet stays same; assignments may now have dept corrected.

            // 2) Delete unrecoverable orphan/bad rows

            var delAq = db.Assignments.AsQueryable();
            if (scopeDepartmentId is not null)
                delAq = delAq.Where(a => a.DepartmentId == scopeDepartmentId.Value);

            var toDeleteAssignments = await delAq.Where(a =>
                a.DepartmentId <= 0 ||
                a.CourseId <= 0 ||
                a.SlotId <= 0 ||
                a.FacultyId <= 0 ||
                a.SectionIndex < 0 ||
                !deptSet.Contains(a.DepartmentId) ||
                !courseSet.Contains(a.CourseId) ||
                !slotSet.Contains(a.SlotId) ||
                !facultySet.Contains(a.FacultyId)
            ).ToListAsync();

            if (toDeleteAssignments.Count > 0)
            {
                db.Assignments.RemoveRange(toDeleteAssignments);
                aDeleted = toDeleteAssignments.Count;
                await db.SaveChangesAsync();
            }

            // Blocks: delete orphan/bad
            var delBq = db.FacultySlotBlocks.AsQueryable();
            if (scopeDepartmentId is not null)
            {
                var deptId = scopeDepartmentId.Value;
                var allowedFacultyIds = await db.Faculties.AsNoTracking()
                    .Where(f => f.DepartmentId == deptId)
                    .Select(f => f.Id)
                    .ToListAsync();
                var allowedSet = allowedFacultyIds.ToHashSet();
                delBq = delBq.Where(b => allowedSet.Contains(b.FacultyId));
            }

            var toDeleteBlocks = await delBq.Where(b =>
                b.FacultyId <= 0 ||
                b.SlotId <= 0 ||
                !facultySet.Contains(b.FacultyId) ||
                !slotSet.Contains(b.SlotId)
            ).ToListAsync();

            if (toDeleteBlocks.Count > 0)
            {
                db.FacultySlotBlocks.RemoveRange(toDeleteBlocks);
                bDeleted = toDeleteBlocks.Count;
                await db.SaveChangesAsync();
            }

            if (aRepaired > 0 || aDeleted > 0 || bDeleted > 0)
            {
                var rev = await BumpServerRevisionAsync();
                newRev = rev.Revision;
            }

            // Re-run diagnosis numbers (optional). We'll keep original diagnosis and include applied counts.
        }

        var report = new LegacyMaintenanceReport(
            RanUtc: DateTime.UtcNow,
            AppliedFix: applyFix,
            AssignmentsTotal: aTotal,
            AssignmentsBadKey: aBadKey,
            AssignmentsOrphanDepartment: aOrDept,
            AssignmentsOrphanCourse: aOrCourse,
            AssignmentsOrphanSlot: aOrSlot,
            AssignmentsOrphanFaculty: aOrFac,
            AssignmentsDeptMismatchCourse: aMismatch,
            AssignmentsDeptRepaired: aRepaired,
            AssignmentsDeleted: aDeleted,
            BlocksTotal: bTotal,
            BlocksBadKey: bBadKey,
            BlocksOrphanSlot: bOrSlot,
            BlocksOrphanFaculty: bOrFac,
            BlocksDeleted: bDeleted,
            NewRevision: newRev
        );

        return (true, string.Empty, report);
    }
    catch (Exception ex)
    {
        var report = new LegacyMaintenanceReport(
            RanUtc: DateTime.UtcNow,
            AppliedFix: applyFix,
            AssignmentsTotal: 0,
            AssignmentsBadKey: 0,
            AssignmentsOrphanDepartment: 0,
            AssignmentsOrphanCourse: 0,
            AssignmentsOrphanSlot: 0,
            AssignmentsOrphanFaculty: 0,
            AssignmentsDeptMismatchCourse: 0,
            AssignmentsDeptRepaired: 0,
            AssignmentsDeleted: 0,
            BlocksTotal: 0,
            BlocksBadKey: 0,
            BlocksOrphanSlot: 0,
            BlocksOrphanFaculty: 0,
            BlocksDeleted: 0,
            NewRevision: null
        );

        return (false, ex.Message, report);
    }
}

// -------------------------
        // -------------------------

        public async Task<List<AssignmentEntity>> GetAssignmentsEntitiesAsync(int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var q = db.Assignments.AsNoTracking().AsQueryable();
            if (scopeDepartmentId is not null)
                q = q.Where(a => a.DepartmentId == scopeDepartmentId.Value);

            return await q.OrderBy(a => a.DepartmentId)
                .ThenBy(a => a.CourseId)
                .ThenBy(a => a.SectionIndex)
                .ThenBy(a => a.SlotId)
                .ToListAsync();
        }

        public async Task<(bool ok, string errorMessage)> ReplaceAssignmentsEntitiesAsync(List<AssignmentEntity> rows, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var now = DateTime.UtcNow;

                rows ??= new List<AssignmentEntity>();

                if (scopeDepartmentId is not null)
                {
                    var deptId = scopeDepartmentId.Value;

                    // Enforce scope: the client must not push other departments.
                    rows = rows.Where(r => r.DepartmentId == deptId).ToList();
                }

                if (rows.Count == 0)
                    return (false, "There are no results to save.");

                // ------------------------------
                // 4B-5a: Replace strategy MUST be scoped by (DepartmentId + Level)
                // so pushing Level-2 results does not wipe Level-1 for the same department.
                // We infer levels from server Courses table (CourseId -> Level).
                // ------------------------------

                var distinctCourseIds = rows.Select(r => r.CourseId).Distinct().ToList();

                // Fetch course info once
                var courseInfo = await db.Courses.AsNoTracking()
                    .Where(c => distinctCourseIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.DepartmentId, c.Level })
                    .ToListAsync();

                var courseById = courseInfo.ToDictionary(x => x.Id, x => x);

                // Validate: all CourseIds must exist on the server
                var missing = distinctCourseIds.Where(id => !courseById.ContainsKey(id)).Take(10).ToList();
                if (missing.Count > 0)
                    return (false, $"Unable to save the results because some courses do not exist on the server. Examples: {string.Join(", ", missing)}");

                // Build affected dept->levels map (from server courses)
                var affected = new Dictionary<int, HashSet<int>>();
                foreach (var r in rows)
                {
                    var info = courseById[r.CourseId];

                    // Strong validation: DeptId in payload should match the canonical DeptId in server Courses.
                    if (r.DepartmentId != info.DepartmentId)
                        return (false, $"Unable to save the results: course (CourseId={r.CourseId}) belongs to department (DepartmentId={info.DepartmentId}), but the result row contains (DepartmentId={r.DepartmentId}).");

                    if (!affected.TryGetValue(r.DepartmentId, out var levels))
                    {
                        levels = new HashSet<int>();
                        affected[r.DepartmentId] = levels;
                    }
                    levels.Add(info.Level);
                }

                // Normalize SavedUtc and ensure inserts
                foreach (var r in rows)
                {
                    r.Id = 0;
                    r.SavedUtc = now;
                }

                await using var tx = await db.Database.BeginTransactionAsync();

                // Remove only the affected (dept + levels) slice.
                foreach (var kv in affected)
                {
                    var deptId = kv.Key;
                    var levels = kv.Value;

                    var courseIdsAtLevels = await db.Courses.AsNoTracking()
                        .Where(c => c.DepartmentId == deptId && levels.Contains(c.Level))
                        .Select(c => c.Id)
                        .ToListAsync();

                    if (courseIdsAtLevels.Count == 0)
                        continue;

                    var toRemove = db.Assignments.Where(a => a.DepartmentId == deptId && courseIdsAtLevels.Contains(a.CourseId));
                    db.Assignments.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                db.Assignments.AddRange(rows);
                await db.SaveChangesAsync();

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }


        // -------------------------
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> ClearAssignmentsByDeptLevelAsync(int departmentId, int level, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                // Enforce scope: supervisors can clear only their own department.
                if (scopeDepartmentId is not null)
                    departmentId = scopeDepartmentId.Value;

                if (departmentId <= 0)
                    return (false, "Invalid department.", 0);

                if (level <= 0)
                    return (false, "Invalid level.", 0);

                // Resolve CourseIds for this level (across ALL departments).
                // Reason: a MANUAL assignment can be attached to a department even if the
                // Course itself belongs to a different department in the catalog.
                // If we filter courses by DepartmentId here, those cross-department manual
                // assignments will never be cleared.
                var courseIds = await db.Courses.AsNoTracking()
                    .Where(c => c.Level == level)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (courseIds.Count == 0)
                    return (true, string.Empty, 0);

                // Remove ALL rows for this department+level (GENERATED + MANUAL).
                // This is used by the "Clear Entire Department" operation.
                var toRemove = await db.Assignments
                    .Where(a => a.DepartmentId == departmentId
                                && courseIds.Contains(a.CourseId)
                                )
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.Assignments.RemoveRange(toRemove);
                await db.SaveChangesAsync();

                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // - Keeps MANUAL rows.
        // - Used by the supervisor button "Clear Entire Department".
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> ClearGeneratedAssignmentsForDepartmentAsync(
            int departmentId,
            int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                // Enforce scope: supervisors can clear only their own department.
                if (scopeDepartmentId is not null)
                    departmentId = scopeDepartmentId.Value;

                if (departmentId <= 0)
                    return (false, "Invalid department.", 0);

                // Remove generated rows only (keep MANUAL rows)
                var toRemove = await db.Assignments
                    .Where(a => a.DepartmentId == departmentId )
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.Assignments.RemoveRange(toRemove);
                await db.SaveChangesAsync();
                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // - Keeps MANUAL rows.
        // - Admin-only UI action.
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> ClearAllGeneratedAssignmentsAsync()
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var toRemove = await db.Assignments
                    .Where(a => a.Status != "MANUAL")
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.Assignments.RemoveRange(toRemove);
                await db.SaveChangesAsync();
                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // - Keeps MANUAL rows.
        // - Used by the "Delete" button in ResultsGrid so changes apply on the server immediately.
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> DeleteGeneratedAssignmentsForCourseSectionAsync(
            int departmentId,
            int courseId,
            int sectionIndex,
            int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                // Enforce scope: supervisors can delete only their own department.
                if (scopeDepartmentId is not null)
                    departmentId = scopeDepartmentId.Value;

                if (departmentId <= 0)
                    return (false, "Invalid department.", 0);

                if (courseId <= 0)
                    return (false, "Invalid course.", 0);

                if (sectionIndex <= 0)
                    return (false, "Invalid section number.", 0);

                var toRemove = await db.Assignments
                    .Where(a => a.DepartmentId == departmentId
                                && a.CourseId == courseId
                                && a.SectionIndex == sectionIndex
                                )
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.Assignments.RemoveRange(toRemove);
                await db.SaveChangesAsync();
                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // Used by "Faculty Timetable" so that deleting the LAST row is persisted immediately.
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> DeleteAssignmentRowAsync(
            int departmentId,
            int courseId,
            int sectionIndex,
            int slotId,
            int facultyId,
            int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                // Enforce scope: supervisors can delete only their own department.
                if (scopeDepartmentId is not null)
                    departmentId = scopeDepartmentId.Value;

                if (departmentId <= 0)
                    return (false, "Invalid department.", 0);

                if (courseId <= 0)
                    return (false, "Invalid course.", 0);

                if (sectionIndex <= 0)
                    return (false, "Invalid section number.", 0);

                if (slotId <= 0)
                    return (false, "Invalid time slot.", 0);

                if (facultyId <= 0)
                    return (false, "Invalid faculty member.", 0);

                var toRemove = await db.Assignments
                    .Where(a => a.DepartmentId == departmentId
                                && a.CourseId == courseId
                                && a.SectionIndex == sectionIndex
                                && a.SlotId == slotId
                                && a.FacultyId == facultyId)
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.Assignments.RemoveRange(toRemove);
                await db.SaveChangesAsync();
                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // -------------------------
        public async Task<(bool ok, string errorMessage, int removedCount)> DeleteFacultySlotBlockAsync(
            int facultyId,
            int slotId,
            int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                if (facultyId <= 0)
                    return (false, "Invalid faculty member.", 0);

                if (slotId <= 0)
                    return (false, "Invalid time slot.", 0);

                if (scopeDepartmentId is not null)
                {
                    var deptId = scopeDepartmentId.Value;
                    var allowed = await db.Faculties.AsNoTracking()
                        .AnyAsync(f => f.Id == facultyId && f.DepartmentId == deptId);
                    if (!allowed)
                        return (false, "You do not have permission to delete this faculty block/assignment.", 0);
                }

                var toRemove = await db.FacultySlotBlocks
                    .Where(b => b.FacultyId == facultyId && b.SlotId == slotId)
                    .ToListAsync();

                if (toRemove.Count == 0)
                    return (true, string.Empty, 0);

                db.FacultySlotBlocks.RemoveRange(toRemove);
                await db.SaveChangesAsync();
                return (true, string.Empty, toRemove.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        }


        // -------------------------
        // -------------------------

        public async Task<List<FacultySlotBlockEntity>> GetFacultySlotBlockEntitiesAsync(int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            if (scopeDepartmentId is null)
            {
                return await db.FacultySlotBlocks.AsNoTracking()
                    .OrderBy(b => b.FacultyId)
                    .ThenBy(b => b.SlotId)
                    .ToListAsync();
            }

            var deptId = scopeDepartmentId.Value;
            var facultyIds = await db.Faculties.AsNoTracking()
                .Where(f => f.DepartmentId == deptId)
                .Select(f => f.Id)
                .ToListAsync();

            return await db.FacultySlotBlocks.AsNoTracking()
                .Where(b => facultyIds.Contains(b.FacultyId))
                .OrderBy(b => b.FacultyId)
                .ThenBy(b => b.SlotId)
                .ToListAsync();
        }

        public async Task<(bool ok, string errorMessage)> ReplaceFacultySlotBlockEntitiesAsync(List<FacultySlotBlockEntity> rows, int? scopeDepartmentId)
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            try
            {
                var now = DateTime.UtcNow;

                rows ??= new List<FacultySlotBlockEntity>();

                // Determine allowed faculties in scope (when supervisor).
                List<int>? allowedFacultyIds = null;
                if (scopeDepartmentId is not null)
                {
                    var deptId = scopeDepartmentId.Value;
                    allowedFacultyIds = await db.Faculties.AsNoTracking()
                        .Where(f => f.DepartmentId == deptId)
                        .Select(f => f.Id)
                        .ToListAsync();

                    rows = rows.Where(r => allowedFacultyIds.Contains(r.FacultyId)).ToList();
                }

                // Distinct by faculty+slot
                rows = rows.GroupBy(r => new { r.FacultyId, r.SlotId }).Select(g => g.First()).ToList();

                // Normalize
                foreach (var r in rows)
                {
                    r.Id = 0;
                    r.SavedUtc = now;
                }

                await using var tx = await db.Database.BeginTransactionAsync();

                if (scopeDepartmentId is null)
                {
                    db.FacultySlotBlocks.RemoveRange(db.FacultySlotBlocks);
                }
                else
                {
                    var toRemove = db.FacultySlotBlocks.Where(x => allowedFacultyIds!.Contains(x.FacultyId));
                    db.FacultySlotBlocks.RemoveRange(toRemove);
                }

                await db.SaveChangesAsync();

                if (rows.Count > 0)
                {
                    db.FacultySlotBlocks.AddRange(rows);
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }


        // -------------------------
        // -------------------------

        public async Task<bool> AnyUsersAsync()
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);
            return await db.Users.AnyAsync();
        }

        /// <summary>
        /// Create the first Admin user. This will fail if any user already exists.
        /// </summary>
        public async Task<(bool ok, string errorMessage)> CreateFirstAdminAsync(string username, string password)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0) return (false, "A username is required.");
            if (password is null || password.Length < 4) return (false, "The password is too short.");

            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            if (await db.Users.AnyAsync())
                return (false, "Users already exist. The first administrator cannot be created.");

            var (hash, salt) = PasswordHasher.Hash(password);
            db.Users.Add(new UserEntity
            {
                Username = username,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = UserRole.Admin.ToString(),
                DepartmentId = null,
                CreatedUtc = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync();
                return (true, string.Empty);
            }
            catch (DbUpdateException)
            {
                return (false, "This username is already in use.");
            }
        }

        public async Task<(UserSession? session, string errorMessage)> LoginAsync(string username, string password)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0) return (null, "A username is required.");
            if (password is null) return (null, "A password is required.");

            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username);
            if (user is null)
                return (null, "The sign-in credentials are incorrect.");

            if (!PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
                return (null, "The sign-in credentials are incorrect.");

            if (!Enum.TryParse<UserRole>(user.Role, ignoreCase: true, out var role))
                role = UserRole.Supervisor;

            var session = new UserSession(user.Id, user.Username, role, user.DepartmentId);
            return (session, string.Empty);
        }

        // -------------------------
        // -------------------------

        public async Task<List<UserEntity>> GetUsersAsync()
        {
            await InitializeAsync();
            await using var db = new AppDbContext(_options);
            return await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        }

        public async Task<(bool ok, string errorMessage)> CreateUserAsync(
            string username,
            string password,
            UserRole role,
            int? departmentId)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0) return (false, "A username is required.");
            if (password is null || password.Length < 4) return (false, "The password is too short (minimum 4 characters).");

            if (role == UserRole.Supervisor)
            {
                if (departmentId is null || departmentId.Value <= 0)
                    return (false, "Select a department for the supervisor.");
            }
            else
            {
                departmentId = null; // Admin is not scoped
            }

            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            if (await db.Users.AnyAsync(x => x.Username == username))
                return (false, "This username is already in use.");

            // Validate department exists (for supervisors)
            if (role == UserRole.Supervisor)
            {
                var deptExists = await db.Departments.AnyAsync(d => d.Id == departmentId!.Value);
                if (!deptExists) return (false, "The selected department does not exist in the database.");
            }

            var (hash, salt) = PasswordHasher.Hash(password);
            db.Users.Add(new UserEntity
            {
                Username = username,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role.ToString(),
                DepartmentId = departmentId,
                CreatedUtc = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync();
                return (true, string.Empty);
            }
            catch (DbUpdateException)
            {
                return (false, "The user could not be created. Please make sure the username is not duplicated.");
            }
        }

        public async Task<(bool ok, string errorMessage)> ResetPasswordAsync(int userId, string newPassword)
        {
            if (userId <= 0) return (false, "Select a user.");
            if (newPassword is null || newPassword.Length < 4) return (false, "The password is too short (minimum 4 characters).");

            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (user is null) return (false, "The user was not found.");

            var (hash, salt) = PasswordHasher.Hash(newPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            await db.SaveChangesAsync();

            return (true, string.Empty);
        }

        public async Task<(bool ok, string errorMessage)> DeleteUserAsync(int userId)
        {
            if (userId <= 0) return (false, "Select a user.");

            await InitializeAsync();
            await using var db = new AppDbContext(_options);

            var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (user is null) return (false, "The user was not found.");

            // Don't allow deleting the last admin.
            if (string.Equals(user.Role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var adminCount = await db.Users.CountAsync(x => x.Role == UserRole.Admin.ToString());
                if (adminCount <= 1)
                    return (false, "The last system administrator cannot be deleted.");
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return (true, string.Empty);
        }

        /// <summary>
        /// Load core data from DB into Store.
        /// Returns true if at least one of the core tables had data.
        /// </summary>
        public async Task<bool> TryLoadCoreAsync(DataStore store)
        {
            await InitializeAsync();

            await using var db = new AppDbContext(_options);

            var hasAny =
                await db.Departments.AnyAsync() ||
                await db.Courses.AnyAsync() ||
                await db.Faculties.AnyAsync() ||
                await db.Rooms.AnyAsync() ||
                await db.Slots.AnyAsync();

            if (!hasAny) return false;

            // Clear only "core lookups" to avoid accidentally wiping generated results.
            store.Departments.Clear();
            store.Courses.Clear();
            store.Faculties.Clear();
            store.Rooms.Clear();
            store.Slots.Clear();

            var depts = await db.Departments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            foreach (var d in depts)
                store.Departments.Add(new Department(d.Id, d.Name));

            var courses = await db.Courses.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            foreach (var c in courses)
            {
                var m = new Course(c.Id, c.Name, c.DepartmentId, c.Level, c.IsGeneralCourse, c.HoursPerWeek);
                if (!string.IsNullOrWhiteSpace(c.CourseCode))
                    m.CourseCode = c.CourseCode;
                store.Courses.Add(m);
            }

            // If CourseCode is missing on Courses rows, backfill it from the mapping table.
            await LoadCourseCodesFromMapsIntoStoreAsync(db, store);

            var facs = await db.Faculties.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            foreach (var f in facs)
                store.Faculties.Add(new Faculty(f.Id, f.Name, f.DepartmentId, f.IsGeneralStudies));

            var rooms = await db.Rooms.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            foreach (var r in rooms)
                store.Rooms.Add(new Room(r.Id, r.Name, r.DepartmentId));

            var slots = await db.Slots.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            foreach (var s in slots)
            {
                var day = (DayOfWeek)s.Day;
                var start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(s.StartMinutes));
                var end = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(s.EndMinutes));
                store.Slots.Add(new Slot(s.Id, day, start, end));
            }


            await LoadPracticalAndSplitsIntoStoreAsync(db, store);


            // Load CourseFacultyOverrides (fixed instructors per course)
            await LoadCourseFacultyOverridesIntoStoreAsync(db, store);
            await LoadCourseCodeFacultyOverridesIntoStoreAsync(db, store);
            return true;
        }

        /// <summary>
        /// Save core data from Store into DB.
        /// </summary>
        public async Task SaveCoreAsync(DataStore store)
        {
            await InitializeAsync();

            await using var db = new AppDbContext(_options);

            // Replace-all, but keep order stable
            db.Departments.RemoveRange(db.Departments);
            db.Courses.RemoveRange(db.Courses);
            db.Faculties.RemoveRange(db.Faculties);
            db.Rooms.RemoveRange(db.Rooms);
            db.Slots.RemoveRange(db.Slots);
            await db.SaveChangesAsync();

            db.Departments.AddRange(store.Departments.Select(d => new DepartmentEntity { Id = d.Id, Name = d.Name }));
            db.Courses.AddRange(store.Courses.Select(c => new CourseEntity
            {
                Id = c.Id,
                Name = c.Name,
                DepartmentId = c.DepartmentId,
                Level = c.Level,
                IsGeneralCourse = c.IsGeneralCourse,
                HoursPerWeek = c.HoursPerWeek,
                CourseCode = string.IsNullOrWhiteSpace(c.CourseCode) ? null : NormalizeCourseCode(c.CourseCode)
            }));
            db.Faculties.AddRange(store.Faculties.Select(f => new FacultyEntity
            {
                Id = f.Id,
                Name = f.Name,
                DepartmentId = f.DepartmentId,
                IsGeneralStudies = f.IsGeneralStudies
            }));
            db.Rooms.AddRange(store.Rooms.Select(r => new RoomEntity
            {
                Id = r.Id,
                Name = r.Name,
                DepartmentId = r.DepartmentId
            }));
            db.Slots.AddRange(store.Slots.Select(s => new SlotEntity
            {
                Id = s.Id,
                Day = (int)s.Day,
                StartMinutes = (int)s.Start.ToTimeSpan().TotalMinutes,
                EndMinutes = (int)s.End.ToTimeSpan().TotalMinutes
            }));

            await db.SaveChangesAsync();

            // Persist practical flags & hour splits (from JSON import / user toggles)
            await SavePracticalAndSplitsAsync(db, store);

            // Persist CourseFacultyOverrides
            await SaveCourseFacultyOverridesAsync(db, store);

            await SaveCourseCodeFacultyOverridesAsync(db, store);

            // Keep CourseCodeMaps in sync as a helper for expected-registration reports (and backward-compat).
            await SaveCourseCodesIntoMapsAsync(db, store);
        }

        



        // -------------------------
        // -------------------------
        private static async Task LoadPracticalAndSplitsIntoStoreAsync(AppDbContext db, DataStore store)
        {
            // Practical course ids
            store.PracticalCourseIds.Clear();
            store.CourseHourSplits.Clear();

            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // PracticalCourses
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CourseId FROM PracticalCourses";
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    if (!rdr.IsDBNull(0))
                        store.PracticalCourseIds.Add(rdr.GetInt32(0));
                }
            }

            // CourseHourSplits
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CourseId, TheoryHours, PracticalHours, PreferSameInstructor FROM CourseHourSplits";
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var courseId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
                    var th = rdr.IsDBNull(1) ? 0.0 : rdr.GetDouble(1);
                    var pr = rdr.IsDBNull(2) ? 0.0 : rdr.GetDouble(2);
                    var pref = !rdr.IsDBNull(3) && rdr.GetInt32(3) != 0;

                    if (courseId > 0)
                    {
                        store.CourseHourSplits.Add(new CourseHourSplitOverride
                        {
                            CourseId = courseId,
                            TheoryHours = th,
                            PracticalHours = pr,
                            PreferSameInstructor = pref
                        });
                    }
                }
            }
        }

        private static async Task SavePracticalAndSplitsAsync(AppDbContext db, DataStore store)
        {
            // PracticalCourses (replace-all)
            await db.Database.ExecuteSqlRawAsync("DELETE FROM PracticalCourses");
            foreach (var id in store.PracticalCourseIds.Distinct().Where(x => x > 0).OrderBy(x => x))
            {
                await db.Database.ExecuteSqlRawAsync("INSERT OR REPLACE INTO PracticalCourses(CourseId) VALUES({0})", id);
            }

            // CourseHourSplits (replace-all)
            await db.Database.ExecuteSqlRawAsync("DELETE FROM CourseHourSplits");
            var now = DateTime.UtcNow.ToString("O");
            foreach (var s in store.CourseHourSplits
                .Where(x => x.CourseId > 0)
                .GroupBy(x => x.CourseId)
                .Select(g => g.Last())
                .OrderBy(x => x.CourseId))
            {
                var prefer = s.PreferSameInstructor ? 1 : 0;
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO CourseHourSplits(CourseId, TheoryHours, PracticalHours, PreferSameInstructor, UpdatedUtc) VALUES({0},{1},{2},{3},{4})",
                    s.CourseId, s.TheoryHours, s.PracticalHours, prefer, now);
            }
        }

// -------------------------
// -------------------------
private static async Task LoadCourseFacultyOverridesIntoStoreAsync(AppDbContext db, DataStore store)
{
    store.CourseFacultyOverrides.Clear();

    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT CourseId, FacultyId FROM CourseFacultyOverrides";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var courseId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var facultyId = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
        if (courseId > 0 && facultyId > 0)
            store.CourseFacultyOverrides.Add(new CourseFacultyOverride(courseId, facultyId));
    }
}

public async Task SaveCourseFacultyOverridesAsync(DataStore store)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    await SaveCourseFacultyOverridesAsync(db, store);
}

public async Task SaveCourseCodeFacultyOverridesAsync(DataStore store)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    await SaveCourseCodeFacultyOverridesAsync(db, store);
}

private static async Task SaveCourseFacultyOverridesAsync(AppDbContext db, DataStore store)
{
    // Replace-all (deterministic)
    await db.Database.ExecuteSqlRawAsync("DELETE FROM CourseFacultyOverrides");

    var now = DateTime.UtcNow.ToString("O");
    foreach (var o in store.CourseFacultyOverrides
                 .Where(x => x.CourseId > 0 && x.FacultyId > 0)
                 .Distinct()
                 .OrderBy(x => x.CourseId)
                 .ThenBy(x => x.FacultyId))
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT OR REPLACE INTO CourseFacultyOverrides(CourseId, FacultyId, UpdatedUtc) VALUES({0},{1},{2})",
            o.CourseId, o.FacultyId, now);
    }
}



// -------------------------

// -------------------------
// (ScopeDepartmentId=0 means global across all departments)
// -------------------------
private static async Task LoadCourseCodeFacultyOverridesIntoStoreAsync(AppDbContext db, DataStore store)
{
    store.CourseCodeFacultyOverrides.Clear();

    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT ScopeDepartmentId, CourseCode, FacultyId FROM CourseCodeFacultyOverrides";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var scopeDeptId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var code = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
        var facultyId = rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2);

        code = NormalizeCourseCode(code);
        if (scopeDeptId < 0) scopeDeptId = 0;

        if (facultyId > 0 && !string.IsNullOrWhiteSpace(code))
            store.CourseCodeFacultyOverrides.Add(new CourseCodeFacultyOverride(scopeDeptId, code, facultyId));
    }
}

private static async Task SaveCourseCodeFacultyOverridesAsync(AppDbContext db, DataStore store)
{
    // Replace-all (deterministic)
    await db.Database.ExecuteSqlRawAsync("DELETE FROM CourseCodeFacultyOverrides");

    var now = DateTime.UtcNow.ToString("O");

    foreach (var o in store.CourseCodeFacultyOverrides
                 .Select(x => new CourseCodeFacultyOverride(
                     x.ScopeDepartmentId < 0 ? 0 : x.ScopeDepartmentId,
                     NormalizeCourseCode(x.CourseCode),
                     x.FacultyId))
                 .Where(x => x.FacultyId > 0 && !string.IsNullOrWhiteSpace(x.CourseCode))
                 .Distinct()
                 .OrderBy(x => x.ScopeDepartmentId)
                 .ThenBy(x => x.CourseCode)
                 .ThenBy(x => x.FacultyId))
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT OR REPLACE INTO CourseCodeFacultyOverrides(ScopeDepartmentId, CourseCode, FacultyId, UpdatedUtc) VALUES({0},{1},{2},{3})",
            o.ScopeDepartmentId, o.CourseCode, o.FacultyId, now);
    }
}



// CourseCode helper utilities
// - Reports: CourseCodeMaps remains useful for expected-registration mapping
// -------------------------
private static string NormalizeCourseCode(string? code)
{
    if (string.IsNullOrWhiteSpace(code)) return string.Empty;
    // Trim + remove internal spaces, and normalize case (official codes are usually case-insensitive)
    var s = code.Trim();
    s = s.Replace(" ", "");
    return s.ToUpperInvariant();
}

private static async Task LoadCourseCodesFromMapsIntoStoreAsync(AppDbContext db, DataStore store)
{
    // We only need mappings that point to an internal CourseId.
    var maps = await db.CourseCodeMaps.AsNoTracking()
        .Where(x => x.CourseId != null && x.CourseId > 0)
        .OrderBy(x => x.CourseCode)
        .ToListAsync();

    if (maps.Count == 0) return;

    var firstByCourseId = new System.Collections.Generic.Dictionary<int, string>();
    foreach (var m in maps)
    {
        var cid = m.CourseId!.Value;
        if (!firstByCourseId.ContainsKey(cid) && !string.IsNullOrWhiteSpace(m.CourseCode))
            firstByCourseId[cid] = m.CourseCode;
    }

    foreach (var c in store.Courses)
    {
        if (string.IsNullOrWhiteSpace(c.CourseCode) && firstByCourseId.TryGetValue(c.Id, out var code))
            c.CourseCode = code;
    }
}

private static async Task SaveCourseCodesIntoMapsAsync(AppDbContext db, DataStore store)
{
    // We upsert ONLY codes that are provided on courses. We do NOT delete other rows
    // because the CourseCodeMaps table may also contain mappings imported from external reports.
    var nowUtc = DateTime.UtcNow;

    var incoming = store.Courses
        .Where(c => c.Id > 0 && !string.IsNullOrWhiteSpace(c.CourseCode))
        .Select(c => new CourseCodeMapEntity
        {
            CourseCode = NormalizeCourseCode(c.CourseCode),
            CourseId = c.Id,
            CourseName = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name.Trim(),
            Notes = "From Course.CourseCode",
            UpdatedUtc = nowUtc
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.CourseCode))
        .GroupBy(x => x.CourseCode, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();

    if (incoming.Count == 0) return;

    foreach (var it in incoming)
    {
        var existing = await db.CourseCodeMaps.FirstOrDefaultAsync(x => x.CourseCode == it.CourseCode);
        if (existing is null)
        {
            db.CourseCodeMaps.Add(it);
        }
        else
        {
            // Conservative: don't overwrite an existing mapping to a different course.
            if (existing.CourseId != null && existing.CourseId > 0 && existing.CourseId != it.CourseId)
                continue;

            existing.CourseId = it.CourseId;
            existing.CourseName = it.CourseName;
            existing.Notes = it.Notes;
            existing.UpdatedUtc = it.UpdatedUtc;
        }
    }

    await db.SaveChangesAsync();
}




// -------------------------
// -------------------------
public async Task<System.Collections.Generic.List<DepartmentEntity>> GetDepartmentsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.Departments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
}

public async Task<System.Collections.Generic.List<FacultyEntity>> GetFacultiesAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.Faculties.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
}

public async Task<System.Collections.Generic.List<CourseEntity>> GetCoursesAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    var courses = await db.Courses.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

    // In that case, hydrate missing codes from CourseCodeMaps (first mapping per CourseId).
    if (courses.Any(x => string.IsNullOrWhiteSpace(x.CourseCode)))
    {
        var maps = await db.CourseCodeMaps.AsNoTracking()
            .Where(x => x.CourseId != null && x.CourseId > 0)
            .OrderBy(x => x.CourseCode)
            .ToListAsync();

        var firstByCourseId = new System.Collections.Generic.Dictionary<int, string>();
        foreach (var m in maps)
        {
            var cid = m.CourseId!.Value;
            if (!firstByCourseId.ContainsKey(cid) && !string.IsNullOrWhiteSpace(m.CourseCode))
                firstByCourseId[cid] = m.CourseCode;
        }

        foreach (var c in courses)
        {
            if (string.IsNullOrWhiteSpace(c.CourseCode) && firstByCourseId.TryGetValue(c.Id, out var code))
                c.CourseCode = code;
        }
    }

    return courses;
}

public async Task<System.Collections.Generic.List<RoomEntity>> GetRoomsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.Rooms.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
}

public async Task<System.Collections.Generic.List<SlotEntity>> GetSlotsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.Slots.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
}


// -------------------------
// -------------------------
public async Task<System.Collections.Generic.List<int>> GetPracticalCourseIdsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<int>();
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT CourseId FROM PracticalCourses ORDER BY CourseId";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        if (!rdr.IsDBNull(0))
            result.Add(rdr.GetInt32(0));
    }

    return result;
}

public async Task<System.Collections.Generic.List<CourseHourSplitOverride>> GetCourseHourSplitsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<CourseHourSplitOverride>();
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT CourseId, TheoryHours, PracticalHours, PreferSameInstructor FROM CourseHourSplits ORDER BY CourseId";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var courseId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var th = rdr.IsDBNull(1) ? 0.0 : rdr.GetDouble(1);
        var pr = rdr.IsDBNull(2) ? 0.0 : rdr.GetDouble(2);
        var pref = !rdr.IsDBNull(3) && rdr.GetInt32(3) != 0;

        if (courseId > 0)
        {
            result.Add(new CourseHourSplitOverride
            {
                CourseId = courseId,
                TheoryHours = th,
                PracticalHours = pr,
                PreferSameInstructor = pref
            });
        }
    }

    return result;
}

public async Task<System.Collections.Generic.List<CourseFacultyOverride>> GetCourseFacultyOverridesAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<CourseFacultyOverride>();
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT CourseId, FacultyId FROM CourseFacultyOverrides ORDER BY CourseId, FacultyId";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var courseId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var facultyId = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
        if (courseId > 0 && facultyId > 0)
            result.Add(new CourseFacultyOverride(courseId, facultyId));
    }

    return result;
}



public async Task<System.Collections.Generic.List<CourseCodeFacultyOverride>> GetCourseCodeFacultyOverridesAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<CourseCodeFacultyOverride>();
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT ScopeDepartmentId, CourseCode, FacultyId FROM CourseCodeFacultyOverrides ORDER BY ScopeDepartmentId, CourseCode, FacultyId";
    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var scopeDeptId = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var code = rdr.IsDBNull(1) ? null : rdr.GetString(1);
        var facultyId = rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2);
        if (!string.IsNullOrWhiteSpace(code) && facultyId > 0)
            result.Add(new CourseCodeFacultyOverride(scopeDeptId, code, facultyId));
    }

    return result;
}




// -------------------------
// -------------------------
public async Task<System.Collections.Generic.List<StudentEntity>> GetStudentsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.Students.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
}


// -------------------------
// -------------------------
public async Task<System.Collections.Generic.List<StudentLiteRow>> GetStudentsLiteByDepartmentAsync(
    int departmentId,
    string? searchText = null,
    int skip = 0,
    int take = 200)
{
    if (departmentId <= 0) return new System.Collections.Generic.List<StudentLiteRow>();

    if (take <= 0) take = 200;
    if (take > 2000) take = 2000;
    if (skip < 0) skip = 0;

    searchText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();

    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<StudentLiteRow>();

    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();

    var sb = new System.Text.StringBuilder();
    sb.Append("SELECT Id, StudentNo, FullName, Level FROM Students WHERE DepartmentId = @deptId ");
    if (searchText is not null)
        sb.Append("AND (FullName LIKE @like OR StudentNo LIKE @like) ");
    sb.Append("ORDER BY FullName LIMIT @take OFFSET @skip;");
    cmd.CommandText = sb.ToString();

    var pDept = cmd.CreateParameter();
    pDept.ParameterName = "@deptId";
    pDept.Value = departmentId;
    cmd.Parameters.Add(pDept);

    if (searchText is not null)
    {
        var pLike = cmd.CreateParameter();
        pLike.ParameterName = "@like";
        pLike.Value = "%" + searchText + "%";
        cmd.Parameters.Add(pLike);
    }

    var pTake = cmd.CreateParameter();
    pTake.ParameterName = "@take";
    pTake.Value = take;
    cmd.Parameters.Add(pTake);

    var pSkip = cmd.CreateParameter();
    pSkip.ParameterName = "@skip";
    pSkip.Value = skip;
    cmd.Parameters.Add(pSkip);

    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var id = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        var studentNo = rdr.IsDBNull(1) ? null : rdr.GetString(1);
        var fullName = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2);
        int? level = rdr.IsDBNull(3) ? null : rdr.GetInt32(3);

        if (id > 0 && !string.IsNullOrWhiteSpace(fullName))
            result.Add(new StudentLiteRow(id, studentNo, fullName, level));
    }

    return result;
}

public async Task<System.Collections.Generic.List<StudentWeeklyAssignmentRow>> GetStudentWeeklyAssignmentsAsync(int studentId)
{
    if (studentId <= 0) return new System.Collections.Generic.List<StudentWeeklyAssignmentRow>();

    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var result = new System.Collections.Generic.List<StudentWeeklyAssignmentRow>();

    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
SELECT
    a.Id,
    a.DepartmentId,
    a.CourseId,
    a.SectionIndex,
    a.SlotId,
    a.FacultyId,
    a.RoomId,
    a.Status,
    a.Kind,
    COALESCE(s.Day, -1) AS SlotDay,
    COALESCE(s.StartMinutes, -1) AS StartMinutes,
    COALESCE(s.EndMinutes, -1) AS EndMinutes
FROM StudentEnrollments e
JOIN Assignments a ON a.Id = e.AssignmentId
LEFT JOIN Slots s ON s.Id = a.SlotId
WHERE e.StudentId = @studentId
ORDER BY SlotDay, StartMinutes, a.CourseId, a.SectionIndex;";

    var p = cmd.CreateParameter();
    p.ParameterName = "@studentId";
    p.Value = studentId;
    cmd.Parameters.Add(p);

    using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        var assignmentId = rdr.IsDBNull(0) ? 0 : rdr.GetInt64(0);
        var deptId = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
        var courseId = rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2);
        var sectionIndex = rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3);
        var slotId = rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4);
        var facultyId = rdr.IsDBNull(5) ? 0 : rdr.GetInt32(5);
        int? roomId = rdr.IsDBNull(6) ? null : rdr.GetInt32(6);
        var status = rdr.IsDBNull(7) ? string.Empty : rdr.GetString(7);
        var kind = rdr.IsDBNull(8) ? string.Empty : rdr.GetString(8);
        var slotDay = rdr.IsDBNull(9) ? -1 : rdr.GetInt32(9);
        var startMin = rdr.IsDBNull(10) ? -1 : rdr.GetInt32(10);
        var endMin = rdr.IsDBNull(11) ? -1 : rdr.GetInt32(11);

        if (assignmentId > 0)
            result.Add(new StudentWeeklyAssignmentRow(
                assignmentId,
                deptId,
                courseId,
                sectionIndex,
                slotId,
                facultyId,
                roomId,
                status,
                kind,
                slotDay,
                startMin,
                endMin));
    }

    return result;
}

public async Task<System.Collections.Generic.List<StudentPlanEntity>> GetStudentPlansByStudentIdAsync(int studentId)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.StudentPlans.AsNoTracking()
        .Where(x => x.StudentId == studentId)
        .OrderBy(x => x.Priority)
        .ThenBy(x => x.CourseId)
        .ToListAsync();
}

public async Task SaveStudentsAdminAsync(System.Collections.Generic.IEnumerable<StudentEntity> students)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = students
        .Where(s => s is not null)
        .Select(s => new StudentEntity
        {
            Id = s.Id,
            StudentNo = string.IsNullOrWhiteSpace(s.StudentNo) ? null : s.StudentNo!.Trim(),
            FullName = (s.FullName ?? string.Empty).Trim(),
            DepartmentId = s.DepartmentId,
            Level = s.Level,
            CreatedUtc = s.CreatedUtc == default ? DateTime.UtcNow : s.CreatedUtc
        })
        .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.FullName))
        .GroupBy(s => s.Id)
        .Select(g => g.Last())
        .ToList();

    var incomingIds = incoming.Select(s => s.Id).ToHashSet();

    var existingStudents = await db.Students.ToListAsync();
    var existingIds = existingStudents.Select(s => s.Id).ToHashSet();

    var removedIds = existingIds.Where(id => !incomingIds.Contains(id)).ToList();
    if (removedIds.Count > 0)
    {
        var plansToDelete = await db.StudentPlans.Where(x => removedIds.Contains(x.StudentId)).ToListAsync();
        var enrollmentsToDelete = await db.StudentEnrollments.Where(x => removedIds.Contains(x.StudentId)).ToListAsync();
        var unassignedToDelete = await db.StudentUnassigned.Where(x => removedIds.Contains(x.StudentId)).ToListAsync();
        var studentsToDelete = existingStudents.Where(x => removedIds.Contains(x.Id)).ToList();

        if (plansToDelete.Count > 0) db.StudentPlans.RemoveRange(plansToDelete);
        if (enrollmentsToDelete.Count > 0) db.StudentEnrollments.RemoveRange(enrollmentsToDelete);
        if (unassignedToDelete.Count > 0) db.StudentUnassigned.RemoveRange(unassignedToDelete);
        if (studentsToDelete.Count > 0) db.Students.RemoveRange(studentsToDelete);
    }

    foreach (var row in incoming)
    {
        var existing = existingStudents.FirstOrDefault(x => x.Id == row.Id);
        if (existing is null)
        {
            db.Students.Add(row);
            continue;
        }

        existing.StudentNo = row.StudentNo;
        existing.FullName = row.FullName;
        existing.DepartmentId = row.DepartmentId;
        existing.Level = row.Level;
        if (existing.CreatedUtc == default)
            existing.CreatedUtc = row.CreatedUtc;
    }

    await db.SaveChangesAsync();
}

public async Task DeleteStudentAdminAsync(int studentId)
{
    if (studentId <= 0) return;

    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var plans = await db.StudentPlans.Where(x => x.StudentId == studentId).ToListAsync();
    var enrollments = await db.StudentEnrollments.Where(x => x.StudentId == studentId).ToListAsync();
    var unassigned = await db.StudentUnassigned.Where(x => x.StudentId == studentId).ToListAsync();
    var student = await db.Students.FirstOrDefaultAsync(x => x.Id == studentId);

    if (plans.Count > 0) db.StudentPlans.RemoveRange(plans);
    if (enrollments.Count > 0) db.StudentEnrollments.RemoveRange(enrollments);
    if (unassigned.Count > 0) db.StudentUnassigned.RemoveRange(unassigned);
    if (student is not null) db.Students.Remove(student);

    await db.SaveChangesAsync();
}

public async Task SaveStudentPlansForStudentAsync(int studentId, System.Collections.Generic.IEnumerable<StudentPlanEntity> plans)
{
    if (studentId <= 0) return;

    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var existingStudent = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId);
    if (existingStudent is null)
        throw new InvalidOperationException("The selected student record must be saved before saving a study plan.");

    var normalized = plans
        .Where(p => p is not null && p.StudentId == studentId && p.CourseId > 0)
        .Select(p => new StudentPlanEntity
        {
            StudentId = studentId,
            CourseId = p.CourseId,
            Priority = p.Priority,
            IsRepeat = p.IsRepeat,
            TermKey = AcademicTermKeyService.NormalizeForStorage(p.TermKey),
            CreatedUtc = p.CreatedUtc == default ? DateTime.UtcNow : p.CreatedUtc
        })
        .GroupBy(p => p.CourseId)
        .Select(g => g.Last())
        .ToList();

    var courses = await db.Courses.AsNoTracking().ToListAsync();
    var departments = await db.Departments.AsNoTracking().ToListAsync();
    var validation = StudentAcademicRegistrationService.ValidatePlan(
        existingStudent,
        normalized.Select(p => new StudentAcademicPlanInput(p.CourseId, p.Priority, p.IsRepeat, p.TermKey)),
        courses,
        departments);

    if (validation.Errors.Count > 0)
        throw new InvalidOperationException(string.Join(" ", validation.Errors.Distinct(StringComparer.OrdinalIgnoreCase)));

    var existing = await db.StudentPlans.Where(x => x.StudentId == studentId).ToListAsync();
    if (existing.Count > 0)
        db.StudentPlans.RemoveRange(existing);

    if (normalized.Count > 0)
        db.StudentPlans.AddRange(normalized);

    await db.SaveChangesAsync();
}


public sealed record StudentImportSaveReport(
    int StudentsCreated,
    int StudentsUpdated,
    int PlansInserted,
    int PlansReplaced,
    System.Collections.Generic.List<string> Warnings);

public async Task<StudentImportSaveReport> ImportStudentsAndPlansAsync(System.Collections.Generic.IEnumerable<StudentImportItem> items)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    await using var tx = await db.Database.BeginTransactionAsync();

    var warnings = new System.Collections.Generic.List<string>();
    var incoming = items?
        .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.StudentNo) && !string.IsNullOrWhiteSpace(x.StudentName) && x.DepartmentId > 0)
        .GroupBy(x => x.StudentNo.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Last())
        .ToList() ?? new System.Collections.Generic.List<StudentImportItem>();

    if (incoming.Count == 0)
        return new StudentImportSaveReport(0, 0, 0, 0, warnings);

    var departments = await db.Departments.AsNoTracking().ToListAsync();
    var courses = await db.Courses.AsNoTracking().ToListAsync();
    var courseById = courses.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Last());

    var studentNos = incoming.Select(x => x.StudentNo.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var existingStudents = await db.Students.Where(x => x.StudentNo != null && studentNos.Contains(x.StudentNo)).ToListAsync();
    var existingByStudentNo = existingStudents
        .Where(x => !string.IsNullOrWhiteSpace(x.StudentNo))
        .GroupBy(x => x.StudentNo!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    var nextStudentId = (await db.Students.Select(x => (int?)x.Id).MaxAsync() ?? 0) + 1;
    var nextPlanId = (await db.StudentPlans.Select(x => (long?)x.Id).MaxAsync() ?? 0L) + 1L;

    var studentsCreated = 0;
    var studentsUpdated = 0;
    var plansInserted = 0;
    var plansReplaced = 0;

    foreach (var item in incoming)
    {
        var studentNo = item.StudentNo.Trim();
        if (!existingByStudentNo.TryGetValue(studentNo, out var student))
        {
            student = new StudentEntity
            {
                Id = nextStudentId++,
                StudentNo = studentNo,
                FullName = item.StudentName.Trim(),
                DepartmentId = item.DepartmentId,
                Level = item.Level,
                CreatedUtc = System.DateTime.UtcNow
            };
            db.Students.Add(student);
            existingByStudentNo[studentNo] = student;
            studentsCreated++;
        }
        else
        {
            student.FullName = item.StudentName.Trim();
            student.DepartmentId = item.DepartmentId;
            student.Level = item.Level;
            if (string.IsNullOrWhiteSpace(student.StudentNo))
                student.StudentNo = studentNo;
            if (student.CreatedUtc == default)
                student.CreatedUtc = System.DateTime.UtcNow;
            studentsUpdated++;
        }

        if (item.Plans.Count == 0)
            continue;

        var normalizedPlans = item.Plans
            .Where(p => p.CourseId > 0)
            .GroupBy(p => p.CourseId)
            .Select(g => g.Last())
            .ToList();

        var validation = StudentAcademicRegistrationService.ValidatePlan(
            student,
            normalizedPlans.Select(p => new StudentAcademicPlanInput(p.CourseId, p.Priority, p.IsRepeat, p.TermKey)),
            courses,
            departments);

        var allowedPlanRows = new System.Collections.Generic.List<StudentImportPlanItem>();
        foreach (var plan in normalizedPlans)
        {
            if (!courseById.TryGetValue(plan.CourseId, out var course))
            {
                warnings.Add($"Student {studentNo}: course ID {plan.CourseId} was skipped because it is not available in the current course list.");
                continue;
            }

            if (!StudentAcademicRegistrationService.IsCourseAllowedForStudent(student, course, departments))
            {
                warnings.Add($"Student {studentNo}: course '{course.Name}' was skipped because it is not valid for the student's department.");
                continue;
            }

            if (!StudentAcademicRegistrationService.IsCourseWithinLevelCeiling(student.Level, course))
            {
                warnings.Add($"Student {studentNo}: course '{course.Name}' was skipped because it is above the student's current academic level.");
                continue;
            }

            if (plan.Priority < 0)
            {
                warnings.Add($"Student {studentNo}: course '{course.Name}' had a negative priority and was reset to 0.");
                allowedPlanRows.Add(plan with { Priority = 0 });
                continue;
            }

            allowedPlanRows.Add(plan);
        }

        warnings.AddRange(validation.Warnings);

        if (allowedPlanRows.Count == 0)
        {
            warnings.Add($"Student {studentNo}: no eligible study-plan rows remained after smart registration checks, so the existing study plan was kept unchanged.");
            continue;
        }

        var existingPlans = await db.StudentPlans.Where(x => x.StudentId == student.Id).ToListAsync();
        if (existingPlans.Count > 0)
        {
            plansReplaced += existingPlans.Count;
            db.StudentPlans.RemoveRange(existingPlans);
        }

        foreach (var plan in allowedPlanRows)
        {
            db.StudentPlans.Add(new StudentPlanEntity
            {
                Id = nextPlanId++,
                StudentId = student.Id,
                CourseId = plan.CourseId,
                Priority = plan.Priority < 0 ? 0 : plan.Priority,
                IsRepeat = plan.IsRepeat,
                TermKey = AcademicTermKeyService.NormalizeForStorage(plan.TermKey),
                CreatedUtc = System.DateTime.UtcNow
            });
            plansInserted++;
        }
    }

    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return new StudentImportSaveReport(studentsCreated, studentsUpdated, plansInserted, plansReplaced, warnings);
}

// -------------------------
// -------------------------
public async Task<System.Collections.Generic.List<CourseCodeMapEntity>> GetCourseCodeMapsAsync()
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);
    return await db.CourseCodeMaps.AsNoTracking().OrderBy(x => x.CourseCode).ToListAsync();
}

/// <summary>
/// Upserts course code mappings. If deleteMissing=true, rows not present in <paramref name="items"/> are deleted.
/// </summary>
public async Task SaveCourseCodeMapsAsync(System.Collections.Generic.IEnumerable<CourseCodeMapEntity> items, bool deleteMissing = false)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = items
        .Where(x => x is not null)
        .Select(x => new CourseCodeMapEntity
        {
            CourseCode = (x.CourseCode ?? string.Empty).Trim(),
            CourseId = (x.CourseId is null || x.CourseId <= 0) ? null : x.CourseId,
            CourseName = string.IsNullOrWhiteSpace(x.CourseName) ? null : x.CourseName!.Trim(),
            Notes = string.IsNullOrWhiteSpace(x.Notes) ? null : x.Notes!.Trim(),
            UpdatedUtc = DateTime.UtcNow
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.CourseCode))
        .GroupBy(x => x.CourseCode)
        .Select(g => g.Last())
        .ToList();

    if (deleteMissing)
    {
        var incomingCodes = incoming.Select(x => x.CourseCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toDelete = await db.CourseCodeMaps.Where(x => !incomingCodes.Contains(x.CourseCode)).ToListAsync();
        if (toDelete.Count > 0) db.CourseCodeMaps.RemoveRange(toDelete);
    }

    foreach (var it in incoming)
    {
        var existing = await db.CourseCodeMaps.FirstOrDefaultAsync(x => x.CourseCode == it.CourseCode);
        if (existing is null)
        {
            db.CourseCodeMaps.Add(it);
        }
        else
        {
            existing.CourseId = it.CourseId;
            existing.CourseName = it.CourseName;
            existing.Notes = it.Notes;
            existing.UpdatedUtc = it.UpdatedUtc;
        }
    }

    await db.SaveChangesAsync();
}

public async Task SaveDepartmentsAsync(System.Collections.Generic.IEnumerable<DepartmentEntity> items)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = items
        .Where(x => x is not null)
        .Select(x => new DepartmentEntity { Id = x.Id, Name = (x.Name ?? "").Trim() })
        .Where(x => x.Id > 0)
        .ToList();

    // delete removed
    var incomingIds = incoming.Select(x => x.Id).ToHashSet();
    var toDelete = await db.Departments.Where(x => !incomingIds.Contains(x.Id)).ToListAsync();
    if (toDelete.Count > 0) db.Departments.RemoveRange(toDelete);

    foreach (var it in incoming)
    {
        var existing = await db.Departments.FirstOrDefaultAsync(x => x.Id == it.Id);
        if (existing is null)
            db.Departments.Add(it);
        else
            existing.Name = it.Name;
    }

    await db.SaveChangesAsync();
}

public async Task SaveFacultiesAsync(System.Collections.Generic.IEnumerable<FacultyEntity> items)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = items
        .Where(x => x is not null)
        .Select(x => new FacultyEntity
        {
            Id = x.Id,
            Name = (x.Name ?? "").Trim(),
            DepartmentId = x.DepartmentId,
            IsGeneralStudies = x.IsGeneralStudies
        })
        .Where(x => x.Id > 0)
        .ToList();

    var incomingIds = incoming.Select(x => x.Id).ToHashSet();
    var toDelete = await db.Faculties.Where(x => !incomingIds.Contains(x.Id)).ToListAsync();
    if (toDelete.Count > 0) db.Faculties.RemoveRange(toDelete);

    foreach (var it in incoming)
    {
        var existing = await db.Faculties.FirstOrDefaultAsync(x => x.Id == it.Id);
        if (existing is null)
            db.Faculties.Add(it);
        else
        {
            existing.Name = it.Name;
            existing.DepartmentId = it.DepartmentId;
            existing.IsGeneralStudies = it.IsGeneralStudies;
        }
    }

    await db.SaveChangesAsync();
}

public async Task SaveCoursesAsync(System.Collections.Generic.IEnumerable<CourseEntity> items)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = items
        .Where(x => x is not null)
        .Select(x => new CourseEntity
        {
            Id = x.Id,
            Name = (x.Name ?? "").Trim(),
            DepartmentId = x.DepartmentId,
            Level = x.Level,
            IsGeneralCourse = x.IsGeneralCourse,
            HoursPerWeek = x.HoursPerWeek,

            CourseCode = string.IsNullOrWhiteSpace(x.CourseCode) ? null : NormalizeCourseCode(x.CourseCode)
        })
        .Where(x => x.Id > 0)
        .ToList();

    var incomingIds = incoming.Select(x => x.Id).ToHashSet();
    var toDelete = await db.Courses.Where(x => !incomingIds.Contains(x.Id)).ToListAsync();
    if (toDelete.Count > 0) db.Courses.RemoveRange(toDelete);

    foreach (var it in incoming)
    {
        var existing = await db.Courses.FirstOrDefaultAsync(x => x.Id == it.Id);
        if (existing is null)
            db.Courses.Add(it);
        else
        {
            existing.Name = it.Name;
            existing.DepartmentId = it.DepartmentId;
            existing.Level = it.Level;
            existing.IsGeneralCourse = it.IsGeneralCourse;
            existing.HoursPerWeek = it.HoursPerWeek;

            existing.CourseCode = it.CourseCode;
        }
    }

    await db.SaveChangesAsync();
}



public async Task SaveRoomsAsync(System.Collections.Generic.IEnumerable<RoomEntity> items)
{
    await InitializeAsync();
    await using var db = new AppDbContext(_options);

    var incoming = items
        .Where(x => x is not null)
        .Select(x => new RoomEntity
        {
            Id = x.Id,
            Name = (x.Name ?? "").Trim(),
            DepartmentId = x.DepartmentId
        })
        .Where(x => x.Id > 0)
        .ToList();

    var incomingIds = incoming.Select(x => x.Id).ToHashSet();

    var toDelete = await db.Rooms.Where(x => !incomingIds.Contains(x.Id)).ToListAsync();
    if (toDelete.Count > 0) db.Rooms.RemoveRange(toDelete);

    foreach (var it in incoming)
    {
        var existing = await db.Rooms.FirstOrDefaultAsync(x => x.Id == it.Id);
        if (existing is null)
            db.Rooms.Add(it);
        else
        {
            existing.Name = it.Name;
            existing.DepartmentId = it.DepartmentId;
        }
    }

    await db.SaveChangesAsync();
}



private static string GetDefaultDbPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrainerScheduler");
            return Path.Combine(dir, "app.db");
        }
    }
}
