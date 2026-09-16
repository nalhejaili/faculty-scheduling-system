using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Data.Entities;
using TrainerScheduler.Services;

namespace TrainerScheduler.Network;

public static class LanDataClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Hardening: be tolerant to case differences between server/client.
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task<bool> TryLoadCoreLookupsIntoStoreAsync(DataStore store)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return false;

        var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var snapshot = await GetCoreSnapshotAsync(settings, token);
            if (snapshot is null)
                return false;

            // Apply to in-memory store (Models)
            store.Departments.Clear();
            foreach (var d in snapshot.Departments.OrderBy(x => x.Id))
                store.Departments.Add(new Department(d.Id, d.Name));

            store.Courses.Clear();
            foreach (var c in snapshot.Courses.OrderBy(x => x.Id))
            {
                var course = new Course(c.Id, c.Name, c.DepartmentId, c.Level, c.IsGeneralCourse, c.HoursPerWeek);
                course.CourseCode = c.CourseCode;
                store.Courses.Add(course);
            }

            store.Faculties.Clear();
            foreach (var f in snapshot.Faculties.OrderBy(x => x.Id))
                store.Faculties.Add(new Faculty(f.Id, f.Name, f.DepartmentId, f.IsGeneralStudies));

            store.Rooms.Clear();
            foreach (var r in snapshot.Rooms.OrderBy(x => x.Id))
                store.Rooms.Add(new Room(r.Id, r.Name, r.DepartmentId));

            store.Slots.Clear();
            foreach (var s in snapshot.Slots.OrderBy(x => x.Id))
            {
                var day = (DayOfWeek)s.Day;
                var start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(s.StartMinutes));
                var end = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(s.EndMinutes));
                store.Slots.Add(new Slot(s.Id, day, start, end));
            }

            store.PracticalCourseIds.Clear();
            if (snapshot.PracticalCourseIds is not null)
            {
                foreach (var id in snapshot.PracticalCourseIds)
                    if (id > 0) store.PracticalCourseIds.Add(id);
            }

            store.CourseHourSplits.Clear();
            if (snapshot.CourseHourSplits is not null)
            {
                foreach (var split in snapshot.CourseHourSplits)
                {
                    if (split is null) continue;
                    if (split.CourseId <= 0) continue;
                    store.CourseHourSplits.Add(new CourseHourSplitOverride
                    {
                        CourseId = split.CourseId,
                        TheoryHours = split.TheoryHours,
                        PracticalHours = split.PracticalHours,
                        PreferSameInstructor = split.PreferSameInstructor
                    });
                }
            }


                        store.CourseFacultyOverrides.Clear();
                        if (snapshot.CourseFacultyOverrides is not null)
                        {
                            foreach (var o in snapshot.CourseFacultyOverrides)
                            {
                                if (o.CourseId > 0 && o.FacultyId > 0)
                                    store.CourseFacultyOverrides.Add(new CourseFacultyOverride(o.CourseId, o.FacultyId));
                            }
                        }

                        store.CourseCodeFacultyOverrides.Clear();
                        if (snapshot.CourseCodeFacultyOverrides is not null)
                        {
                            foreach (var o in snapshot.CourseCodeFacultyOverrides)
                            {
                                if (o.FacultyId > 0 && !string.IsNullOrWhiteSpace(o.CourseCode))
                                    store.CourseCodeFacultyOverrides.Add(new CourseCodeFacultyOverride(o.ScopeDepartmentId, o.CourseCode, o.FacultyId));
                            }
                        }


            return true;
        }
        catch (Exception ex)
        {
            LanLog.Warn($"LAN TryLoadCoreLookupsIntoStoreAsync failed: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }


    private sealed record RevisionDto(long Revision, string UpdatedUtc);

    public static async Task<(long revision, DateTime updatedUtc)?> TryGetRevisionAsync()
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return null;

        var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            using var resp = await GetWithRetryAsync(http, "rev", $"{baseUrl}/rev");
            if (!resp.IsSuccessStatusCode)
            {
                LanLog.Warn($"LAN non-success: {(int)resp.StatusCode} {resp.StatusCode}");
                return null;
            }
var json = await resp.Content.ReadAsStringAsync();
            var dto = JsonSerializer.Deserialize<RevisionDto>(json, _jsonOptions);
            if (dto is null) return null;

            var utc = DateTime.TryParse(dto.UpdatedUtc, out var d) ? d : DateTime.UtcNow;
            LanClientContext.LastKnownRevision = dto.Revision;
            return (dto.Revision, utc);
        }
        catch (Exception ex)
        {
            LanLog.Warn($"LAN TryGetRevisionAsync failed: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    public static async Task<List<AssignmentEntity>?> FetchAssignmentsAsync(int? departmentId = null)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return null;

        var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var url = $"{baseUrl}/data/assignments";
            if (departmentId is not null && departmentId.Value > 0)
                url += $"?departmentId={departmentId.Value}";

            using var resp = await GetWithRetryAsync(http, "fetch-assignments", url);
            if (!resp.IsSuccessStatusCode)
            {
                LanLog.Warn($"LAN non-success: {(int)resp.StatusCode} {resp.StatusCode}");
                return null;
            }
var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<AssignmentEntity>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            LanLog.Warn($"LAN FetchAssignmentsAsync failed: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    public static async Task<List<FacultySlotBlockEntity>?> FetchBlocksAsync(int? departmentId = null)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return null;

        var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var url = $"{baseUrl}/data/blocks";
            if (departmentId is not null && departmentId.Value > 0)
                url += $"?departmentId={departmentId.Value}";

            using var resp = await GetWithRetryAsync(http, "fetch-blocks", url);
            if (!resp.IsSuccessStatusCode)
            {
                LanLog.Warn($"LAN non-success: {(int)resp.StatusCode} {resp.StatusCode}");
                return null;
            }
var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<FacultySlotBlockEntity>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            LanLog.Warn($"LAN FetchBlocksAsync failed: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    private sealed record OkDto(bool Ok, string? ErrorMessage);

    private sealed record ClearAssignmentsDto(bool Ok, string? ErrorMessage, int RemovedCount);

    private sealed record ClearDepartmentDto(bool Ok, string? ErrorMessage, int RemovedCount);

    private sealed record DeleteGeneratedDto(bool Ok, string? ErrorMessage, int RemovedCount);

    private sealed record DeleteRowDto(bool Ok, string? ErrorMessage, int RemovedCount);

    public static async Task<(bool ok, string errorMessage, int removedCount)> ClearAssignmentsByLevelAsync(int departmentId, int level)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.", 0);
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var baseRev = await GetBaseRevisionAsync();

            var payload = JsonSerializer.Serialize(new { departmentId, level }, _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "clear-assignments", $"{baseUrl}/push/clear-assignments", payload, baseRev);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to clear scheduling results on the server.", 0);
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp), 0);
}
            }

            try
            {
                var dto = JsonSerializer.Deserialize<ClearAssignmentsDto>(body, _jsonOptions);
                if (dto is null) return (true, string.Empty, 0);
                if (!dto.Ok) return (false, dto.ErrorMessage ?? "Failed to clear scheduling results on the server.", 0);
                return (true, string.Empty, dto.RemovedCount);
            }
            catch
            {
                return (true, string.Empty, 0);
            }
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
    }


    public static async Task<(bool ok, string errorMessage, int removedCount)> ClearAssignmentsByDepartmentAsync(int departmentId)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.", 0);
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var baseRev = await GetBaseRevisionAsync();

            var payload = JsonSerializer.Serialize(new { departmentId }, _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "clear-department", $"{baseUrl}/push/clear-department", payload, baseRev);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to clear the department timetable on the server.", 0);
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp), 0);
}
            }

            try
            {
                var dto = JsonSerializer.Deserialize<ClearDepartmentDto>(body, _jsonOptions);
                if (dto is null) return (true, string.Empty, 0);
                if (!dto.Ok) return (false, dto.ErrorMessage ?? "Failed to clear the department timetable on the server.", 0);
                return (true, string.Empty, dto.RemovedCount);
            }
            catch
            {
                return (true, string.Empty, 0);
            }
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
    }

    public static async Task<(bool ok, string errorMessage, int removedCount)> DeleteGeneratedForCourseSectionAsync(
        int departmentId,
        int courseId,
        int sectionIndex)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.", 0);
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var baseRev = await GetBaseRevisionAsync();

            var payload = JsonSerializer.Serialize(new { departmentId, courseId, sectionIndex }, _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "delete-generated", $"{baseUrl}/push/delete-generated-for-course", payload);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to delete the course on the server.", 0);
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp), 0);
}
            }

            try
            {
                var dto = JsonSerializer.Deserialize<DeleteGeneratedDto>(body, _jsonOptions);
                if (dto is null) return (true, string.Empty, 0);
                if (!dto.Ok) return (false, dto.ErrorMessage ?? "Failed to delete the course on the server.", 0);
                return (true, string.Empty, dto.RemovedCount);
            }
            catch
            {
                return (true, string.Empty, 0);
            }
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
    }


    // This is required because PushAssignmentsAsync may be skipped when the last row is removed.
    public static async Task<(bool ok, string errorMessage, int removedCount)> DeleteAssignmentRowAsync(
        int departmentId,
        int courseId,
        int sectionIndex,
        int slotId,
        int facultyId)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.", 0);
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var payload = JsonSerializer.Serialize(new { departmentId, courseId, sectionIndex, slotId, facultyId }, _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "delete-assignment-row", $"{baseUrl}/push/delete-assignment-row", payload);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to delete the class entry on the server.", 0);
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp), 0);
}
            }

            try
            {
                var dto = JsonSerializer.Deserialize<DeleteRowDto>(body, _jsonOptions);
                if (dto is null) return (true, string.Empty, 0);
                if (!dto.Ok) return (false, dto.ErrorMessage ?? "Failed to delete the class entry on the server.", 0);
                return (true, string.Empty, dto.RemovedCount);
            }
            catch
            {
                return (true, string.Empty, 0);
            }
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
    }


    public static async Task<(bool ok, string errorMessage, int removedCount)> DeleteBlockAsync(int facultyId, int slotId)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.", 0);
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var payload = JsonSerializer.Serialize(new { facultyId, slotId }, _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "delete-block", $"{baseUrl}/push/delete-block", payload);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to delete the faculty release/assignment record on the server.", 0);
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp), 0);
}
            }

            try
            {
                var dto = JsonSerializer.Deserialize<DeleteRowDto>(body, _jsonOptions);
                if (dto is null) return (true, string.Empty, 0);
                if (!dto.Ok) return (false, dto.ErrorMessage ?? "Failed to delete the faculty release/assignment record on the server.", 0);
                return (true, string.Empty, dto.RemovedCount);
            }
            catch
            {
                return (true, string.Empty, 0);
            }
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex), 0);
        }
    }

    public static async Task<(bool ok, string errorMessage)> PushAssignmentsAsync(List<AssignmentEntity> rows)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.");
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var baseRev = await GetBaseRevisionAsync();

            var json = JsonSerializer.Serialize(rows ?? new List<AssignmentEntity>(), _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "push-assignments", $"{baseUrl}/push/assignments", json, baseRev);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to save scheduling results on the server.");
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp));
}
            }

            return (true, string.Empty);
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex));
}
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex));
}
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex));
}
    }

    public static async Task<(bool ok, string errorMessage)> PushBlocksAsync(List<FacultySlotBlockEntity> rows)
    {
        if (!LanClientContext.IsConnected || LanClientContext.Settings is null || string.IsNullOrWhiteSpace(LanClientContext.Token))
            return (false, "Not connected to the network or not signed in.");
var settings = LanClientContext.Settings;
        var token = LanClientContext.Token!;

        try
        {
            var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";
            using var http = CreateHttp(token);

            var baseRev = await GetBaseRevisionAsync();

            var json = JsonSerializer.Serialize(rows ?? new List<FacultySlotBlockEntity>(), _jsonOptions);
            using var resp = await PostJsonWithRetryAsync(http, "push-blocks", $"{baseUrl}/push/blocks", json, baseRev);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<OkDto>(body, _jsonOptions);
                    return (false, dto?.ErrorMessage ?? "Failed to save faculty release/assignment records on the server.");
                }
                catch
                {
                    return (false, await NonSuccessMessageAsync(resp));
}
            }

            return (true, string.Empty);
        }
        catch (TaskCanceledException ex)
        {
            return (false, LanError.Friendly(ex));
}
        catch (HttpRequestException ex)
        {
            return (false, LanError.Friendly(ex));
}
        catch (Exception ex)
        {
            return (false, LanError.Friendly(ex));
}
    }

    public static async Task<bool> TryPullAssignmentsAndBlocksIntoStoreAsync(DataStore store)
    {
        if (store is null) return false;
        if (!LanClientContext.IsConnected) return false;

        int? scopeDept = null;
        var session = LanClientContext.Session;
        if (session is not null && session.Role == TrainerScheduler.Security.UserRole.Supervisor)
            scopeDept = session.DepartmentId;

        var assigns = await FetchAssignmentsAsync(scopeDept);
        var blocks = await FetchBlocksAsync(scopeDept);

        var any = false;

        if (assigns is not null)
        {
            store.Assignments.Clear();
            foreach (var a in assigns)
            {
                store.Assignments.Add(new Assignment(
                    a.DepartmentId,
                    a.CourseId,
                    a.SectionIndex,
                    a.SlotId,
                    a.FacultyId,
                    a.RoomId,
                    a.Status ?? string.Empty,
                    a.Kind ?? string.Empty));
            }
            any = true;
        }

        if (blocks is not null)
        {
            store.FacultySlotBlocks.Clear();
            foreach (var b in blocks)
            {
                store.FacultySlotBlocks.Add(new FacultySlotBlock
                {
                    FacultyId = b.FacultyId,
                    SlotId = b.SlotId,
                    Reason = b.Reason
                });
            }
            any = true;
        }

        return any;
    }

    public static async Task<(bool ok, string errorMessage)> PushAssignmentsAndBlocksFromStoreAsync(DataStore store)
    {
        if (store is null) return (false, "The data store is not initialized.");
        if (!LanClientContext.IsConnected) return (false, "Not connected to the network or not signed in.");
var rowsA = store.Assignments.Select(a => new AssignmentEntity
        {
            DepartmentId = a.DepartmentId,
            CourseId = a.CourseId,
            SectionIndex = a.SectionIndex,
            SlotId = a.SlotId,
            FacultyId = a.FacultyId,
            RoomId = a.RoomId,
            Status = a.Status ?? string.Empty,
            Kind = a.Kind ?? string.Empty,
            SavedUtc = DateTime.UtcNow
        }).ToList();

        var rowsB = store.FacultySlotBlocks.Select(b => new FacultySlotBlockEntity
        {
            FacultyId = b.FacultyId,
            SlotId = b.SlotId,
            Reason = b.Reason,
            SavedUtc = DateTime.UtcNow
        }).ToList();

        if (rowsA.Count == 0 && rowsB.Count == 0)
            return (false, "There is no data to send.");
// Allow blocks-only updates (e.g., supervisor editing faculty schedule before generating assignments).
        if (rowsA.Count > 0)
        {
            var (okA, errA) = await PushAssignmentsAsync(rowsA);
            if (!okA) return (false, errA);
        }

        if (rowsB.Count > 0)
        {
            var (okB, errB) = await PushBlocksAsync(rowsB);
            if (!okB) return (false, errB);
        }

        return (true, string.Empty);
    }

    
    
    private static async Task<string> NonSuccessMessageAsync(HttpResponseMessage resp)
    {
        string body = string.Empty;
        try
        {
            body = await resp.Content.ReadAsStringAsync();
        }
        catch
        {
            // ignore
        }

        var msg = LanError.Friendly(resp.StatusCode);
        if (!string.IsNullOrWhiteSpace(body))
        {
            var t = body.Trim();
            if (t.Length <= 400) msg += "\n" + t;
        }
        return msg;
    }


    private static async Task<long?> GetBaseRevisionAsync()
    {
        // Prefer last known revision captured by any /rev call.
        if (LanClientContext.LastKnownRevision is long r && r >= 0)
            return r;

        var rev = await TryGetRevisionAsync();
        if (rev is null) return null;
        return rev.Value.revision;
    }

private static Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string opName, string url)
        => LanError.RetryAsync(opName, _ => http.GetAsync(url), maxAttempts: LanError.DefaultMaxAttempts);

    private static Task<HttpResponseMessage> PostJsonWithRetryAsync(HttpClient http, string opName, string url, string json, long? baseRevision = null)
        => LanError.RetryAsync(opName, async _ =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            if (baseRevision is long r && r >= 0)
                req.Headers.TryAddWithoutValidation("X-Base-Rev", r.ToString());
            return await http.SendAsync(req);
        }, maxAttempts: LanError.DefaultMaxAttempts);

    private static HttpClient CreateHttp(string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }


    private static async Task<CoreSnapshot?> GetCoreSnapshotAsync(LanSettings settings, string token)
    {
        var baseUrl = $"http://{settings.ServerHost}:{settings.Port}";

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await GetWithRetryAsync(http, "fetch-core", $"{baseUrl}/data/core");
        if (!resp.IsSuccessStatusCode)
            {
                LanLog.Warn($"LAN non-success: {(int)resp.StatusCode} {resp.StatusCode}");
                return null;
            }
var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CoreSnapshot>(json, _jsonOptions);
    }
}
