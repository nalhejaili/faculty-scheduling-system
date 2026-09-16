using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using TrainerScheduler.Security;
using TrainerScheduler.Services;
using TrainerScheduler.Data.Entities;

namespace TrainerScheduler.Network;

/// <summary>
/// </summary>
public static class LanServerManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly ConcurrentDictionary<string, UserSession> _sessions = new();

    // Server-side SQLite access (Admin machine only)
    private static readonly SqliteStoreService _sqlite = new();

    private static readonly object _gate = new();
    private static IHost? _host;
    private static CancellationTokenSource? _cts;

    public static bool IsRunning
    {
        get
        {
            lock (_gate)
                return _host is not null;
        }
    }

    public static int? Port
    {
        get
        {
            lock (_gate)
                return _host is null ? null : _currentPort;
        }
    }

    private static int _currentPort;

    public static async Task StartAsync(int port)
    {
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        lock (_gate)
        {
            if (_host is not null)
                return;
        }

        var cts = new CancellationTokenSource();

        // Self-host a minimal API on LAN
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls($"http://0.0.0.0:{port}");
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/health", async context =>
                        {
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("ok");
                        });

                        endpoints.MapGet("/info", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";
                            var payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                machine = Environment.MachineName,
                                utc = DateTime.UtcNow.ToString("O"),
                                version = typeof(LanServerManager).Assembly.GetName().Version?.ToString()
                            });
                            await context.Response.WriteAsync(payload);
                        });

                        endpoints.MapPost("/auth/login", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<LoginRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new LoginResponse(false, "Invalid request."), _jsonOptions));
                                    return;
                                }

                                var (session, err) = await _sqlite.LoginAsync(req.Username ?? string.Empty, req.Password ?? string.Empty);
                                if (session is null)
                                {
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new LoginResponse(false, string.IsNullOrWhiteSpace(err) ? "The sign-in details are incorrect." : err), _jsonOptions));
                                    return;
                                }

                                // Create a simple session token (in-memory)
                                var token = Guid.NewGuid().ToString("N");
                                _sessions[token] = session;

                                // NOTE: Named arguments are case-sensitive in C#. Use positional args to avoid typos.
                                var resp = new LoginResponse(
                                    true,
                                    string.Empty,
                                    token,
                                    session.UserId,
                                    session.Username,
                                    session.Role.ToString(),
                                    session.DepartmentId);

                                await context.Response.WriteAsync(JsonSerializer.Serialize(resp, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new LoginResponse(false, ex.Message), _jsonOptions));
                            }
                        });

                        endpoints.MapGet("/data/core", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                // NOTE: We return *all* core lookups. UI restrictions will still apply for Supervisors.
                                var depts = await _sqlite.GetDepartmentsAsync();
                                var courses = await _sqlite.GetCoursesAsync();
                                var facs = await _sqlite.GetFacultiesAsync();
                                var rooms = await _sqlite.GetRoomsAsync();
                                var slots = await _sqlite.GetSlotsAsync();

                                var practicalIds = await _sqlite.GetPracticalCourseIdsAsync();
                                var splits = await _sqlite.GetCourseHourSplitsAsync();
                                var cfo = await _sqlite.GetCourseFacultyOverridesAsync();
                                var ccfoAll = await _sqlite.GetCourseCodeFacultyOverridesAsync();

                                var ccfo = ccfoAll;
                                if (session.Role == UserRole.Supervisor)
                                {
                                    var did = session.DepartmentId;
                                    ccfo = ccfoAll.Where(x => x.ScopeDepartmentId == 0 || x.ScopeDepartmentId == did).ToList();
                                }

                                var snap = new CoreSnapshot
                                {
                                    Departments = depts,
                                    Courses = courses,
                                    Faculties = facs,
                                    Rooms = rooms,
                                    Slots = slots,
                                    PracticalCourseIds = practicalIds,
                                    CourseHourSplits = splits,
                                    CourseFacultyOverrides = cfo,
                                    CourseCodeFacultyOverrides = ccfo
                                };

                                await context.Response.WriteAsync(JsonSerializer.Serialize(snap, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        endpoints.MapGet("/data/assignments", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                int? requestedDept = null;
                                if (context.Request.Query.TryGetValue("departmentId", out var q) && int.TryParse(q.ToString(), out var did) && did > 0)
                                    requestedDept = did;

                                int? scopeDept = null;

                                if (session.Role == UserRole.Supervisor)
                                {
                                    scopeDept = session.DepartmentId;
                                }
                                else
                                {
                                    scopeDept = requestedDept; // admin can optionally filter
                                }

                                var list = await _sqlite.GetAssignmentsEntitiesAsync(scopeDept);
                                await context.Response.WriteAsync(JsonSerializer.Serialize(list, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });

                        endpoints.MapPost("/push/assignments", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var rows = await JsonSerializer.DeserializeAsync<System.Collections.Generic.List<AssignmentEntity>>(context.Request.Body, _jsonOptions)
                                           ?? new System.Collections.Generic.List<AssignmentEntity>();

                                int? scopeDept = null;
                                if (session.Role == UserRole.Supervisor)
                                    scopeDept = session.DepartmentId;

                                var (ok, err) = await _sqlite.ReplaceAssignmentsEntitiesAsync(rows, scopeDept);
                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();

                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        endpoints.MapPost("/push/clear-assignments", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<ClearAssignmentsRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid request." }, _jsonOptions));
                                    return;
                                }

                                var level = req.Level;
                                if (level <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid level." }, _jsonOptions));
                                    return;
                                }

                                // Admin can choose a department; supervisor is forced to their department.
                                int deptId = session.Role == UserRole.Supervisor ? (session.DepartmentId ?? 0) : req.DepartmentId;
                                if (deptId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid department." }, _jsonOptions));
                                    return;
                                }

                                int? scopeDept = session.Role == UserRole.Supervisor ? session.DepartmentId : null;

                                var (ok, err, removed) = await _sqlite.ClearAssignmentsByDeptLevelAsync(deptId, level, scopeDept);
                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();

                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, removedCount = removed }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        // - Supervisor: forced to his department.
                        // - Admin: can clear any department.
                        endpoints.MapPost("/push/clear-department", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<ClearDepartmentRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid request." }, _jsonOptions));
                                    return;
                                }

                                // Admin can choose a department; supervisor is forced to their department.
                                int deptId = session.Role == UserRole.Supervisor ? (session.DepartmentId ?? 0) : req.DepartmentId;
                                if (deptId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid department." }, _jsonOptions));
                                    return;
                                }

                                int? scopeDept = session.Role == UserRole.Supervisor ? session.DepartmentId : null;

                                var (ok, err, removed) = await _sqlite.ClearGeneratedAssignmentsForDepartmentAsync(deptId, scopeDept);
                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, removedCount = removed }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        endpoints.MapPost("/push/delete-generated-for-course", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<DeleteGeneratedForCourseRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid request." }, _jsonOptions));
                                    return;
                                }

                                // Admin can choose a department; supervisor is forced to their department.
                                int deptId = session.Role == UserRole.Supervisor ? (session.DepartmentId ?? 0) : req.DepartmentId;
                                if (deptId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid department." }, _jsonOptions));
                                    return;
                                }

                                if (req.CourseId <= 0 || req.SectionIndex <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid course or section." }, _jsonOptions));
                                    return;
                                }

                                int? scopeDept = session.Role == UserRole.Supervisor ? session.DepartmentId : null;

                                var (ok, err, removed) = await _sqlite.DeleteGeneratedAssignmentsForCourseSectionAsync(
                                    deptId,
                                    req.CourseId,
                                    req.SectionIndex,
                                    scopeDept);

                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();

                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, removedCount = removed }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        endpoints.MapPost("/push/delete-assignment-row", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<DeleteAssignmentRowRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid request." }, _jsonOptions));
                                    return;
                                }

                                // Admin can choose a department; supervisor is forced to their department.
                                int deptId = session.Role == UserRole.Supervisor ? (session.DepartmentId ?? 0) : req.DepartmentId;
                                if (deptId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid department." }, _jsonOptions));
                                    return;
                                }

                                if (req.CourseId <= 0 || req.SectionIndex <= 0 || req.SlotId <= 0 || req.FacultyId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid class-entry data." }, _jsonOptions));
                                    return;
                                }

                                int? scopeDept = session.Role == UserRole.Supervisor ? session.DepartmentId : null;

                                var (ok, err, removed) = await _sqlite.DeleteAssignmentRowAsync(
                                    deptId,
                                    req.CourseId,
                                    req.SectionIndex,
                                    req.SlotId,
                                    req.FacultyId,
                                    scopeDept);

                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, removedCount = removed }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });


                        endpoints.MapPost("/push/delete-block", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var req = await JsonSerializer.DeserializeAsync<DeleteBlockRequest>(context.Request.Body, _jsonOptions);
                                if (req is null)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid request." }, _jsonOptions));
                                    return;
                                }

                                if (req.FacultyId <= 0 || req.SlotId <= 0)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Invalid faculty release/assignment data." }, _jsonOptions));
                                    return;
                                }

                                int? scopeDept = session.Role == UserRole.Supervisor ? session.DepartmentId : null;

                                var (ok, err, removed) = await _sqlite.DeleteFacultySlotBlockAsync(req.FacultyId, req.SlotId, scopeDept);
                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, removedCount = removed }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });



                        endpoints.MapGet("/data/blocks", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                int? requestedDept = null;
                                if (context.Request.Query.TryGetValue("departmentId", out var q) && int.TryParse(q.ToString(), out var did) && did > 0)
                                    requestedDept = did;

                                int? scopeDept = null;

                                if (session.Role == UserRole.Supervisor)
                                {
                                    scopeDept = session.DepartmentId;
                                }
                                else
                                {
                                    scopeDept = requestedDept; // admin can optionally filter
                                }

                                var list = await _sqlite.GetFacultySlotBlockEntitiesAsync(scopeDept);
                                await context.Response.WriteAsync(JsonSerializer.Serialize(list, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });

                        endpoints.MapPost("/push/blocks", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var rows = await JsonSerializer.DeserializeAsync<System.Collections.Generic.List<FacultySlotBlockEntity>>(context.Request.Body, _jsonOptions)
                                           ?? new System.Collections.Generic.List<FacultySlotBlockEntity>();

                                int? scopeDept = null;
                                if (session.Role == UserRole.Supervisor)
                                    scopeDept = session.DepartmentId;

                                var (ok, err) = await _sqlite.ReplaceFacultySlotBlockEntitiesAsync(rows, scopeDept);
                                if (!ok)
                                {
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = err }, _jsonOptions));
                                    return;
                                }

                                await _sqlite.BumpServerRevisionAsync();

                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });

                        endpoints.MapGet("/rev", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";

                            if (!TryAuthorize(context, out var session))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = "Unauthorized." }, _jsonOptions));
                                return;
                            }

                            try
                            {
                                var rev = await _sqlite.GetServerRevisionAsync();
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { revision = rev.Revision, updatedUtc = rev.UpdatedUtc.ToString("O") }, _jsonOptions));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = false, errorMessage = ex.Message }, _jsonOptions));
                            }
                        });
                    });
                });
            })
            .Build();

        await builder.StartAsync(cts.Token);

        lock (_gate)
        {
            _host = builder;
            _cts = cts;
            _currentPort = port;
        }
    }

    public static async Task StopAsync()
    {
        IHost? host;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            host = _host;
            cts = _cts;
            _host = null;
            _cts = null;
            _currentPort = 0;
        }

        if (host is null)
            return;

        try
        {
            cts?.Cancel();
            await host.StopAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            host.Dispose();
            cts?.Dispose();
        }
    }

    
    private static async Task<bool> EnsureFreshAsync(HttpContext context, UserSession session)
    {
        // Admin can always push.
        if (session.Role == UserRole.Admin) return true;

        if (!context.Request.Headers.TryGetValue("X-Base-Rev", out var baseRevValues))
            return true; // backward compatible

        if (!long.TryParse(baseRevValues.ToString(), out var baseRev))
            return true;

        var cur = await _sqlite.GetServerRevisionAsync();
        if (baseRev < cur.Revision)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                ok = false,
                errorMessage = "Your data is out of date. Press Refresh (F5) and try again."
            }, _jsonOptions));
            return false;
        }

        return true;
    }

private static bool TryAuthorize(HttpContext context, out UserSession session)
    {
        session = default!;

        // Accept either "Authorization: Bearer <token>" or "X-Auth-Token: <token>"
        var token = string.Empty;

        if (context.Request.Headers.TryGetValue("Authorization", out var auth))
        {
            var s = auth.ToString();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = s.Substring("Bearer ".Length).Trim();
            else
                token = s.Trim();
        }

        if (string.IsNullOrWhiteSpace(token) && context.Request.Headers.TryGetValue("X-Auth-Token", out var x))
            token = x.ToString().Trim();

        if (string.IsNullOrWhiteSpace(token))
            return false;

        return _sessions.TryGetValue(token, out session!);
    }

    private sealed record ClearAssignmentsRequest(int DepartmentId, int Level);

    private sealed record ClearDepartmentRequest(int DepartmentId);


    private sealed record DeleteGeneratedForCourseRequest(int DepartmentId, int CourseId, int SectionIndex);

    private sealed record DeleteAssignmentRowRequest(int DepartmentId, int CourseId, int SectionIndex, int SlotId, int FacultyId);

    private sealed record DeleteBlockRequest(int FacultyId, int SlotId);

    private sealed record LoginRequest(string? Username, string? Password);

    private sealed record LoginResponse(
        bool Ok,
        string ErrorMessage,
        string? Token = null,
        int? UserId = null,
        string? Username = null,
        string? Role = null,
        int? DepartmentId = null);
}
