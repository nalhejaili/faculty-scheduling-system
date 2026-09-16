# Students, Plans, and Enrollments

## Purpose
The student model stores student profiles and requested courses, then links students to generated timetable assignments through `AssignmentId`.

## Concepts
- **Student**: core student profile.
- **StudentPlan**: a requested/planned course for a student.
- **StudentEnrollment**: assignment of a student to a generated timetable section.

Relationships:
- `StudentPlans.StudentId -> Students.Id`
- `StudentEnrollments.StudentId -> Students.Id`
- `StudentEnrollments.AssignmentId -> Assignments.Id`

`AssignmentId` represents the persisted generated combination of course, section, faculty member, slot, and room.

## SQLite tables

### Students
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | Stable imported/application ID |
| StudentNo | TEXT NULL | Optional student number |
| FullName | TEXT NOT NULL | Student name |
| DepartmentId | INTEGER NULL | Optional department reference |
| Level | INTEGER NULL | Optional academic level |
| CreatedUtc | TEXT NOT NULL | Creation timestamp |

Indexes: `IX_Students_StudentNo`, `IX_Students_DepartmentId`.

### StudentPlans
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | Auto-generated |
| StudentId | INTEGER NOT NULL | Student reference |
| CourseId | INTEGER NOT NULL | Course reference |
| Priority | INTEGER NOT NULL | 0 normal, 1 important, 2 required/high priority |
| IsRepeat | INTEGER NOT NULL | Boolean stored as 0/1 |
| TermKey | TEXT NULL | Optional academic-term label |
| CreatedUtc | TEXT NOT NULL | Creation timestamp |

Indexes: `IX_StudentPlans_StudentId`, `IX_StudentPlans_CourseId`.

### StudentEnrollments
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | Auto-generated |
| StudentId | INTEGER NOT NULL | Student reference |
| AssignmentId | INTEGER NOT NULL | Generated timetable assignment |
| CreatedUtc | TEXT NOT NULL | Creation timestamp |

Indexes: `IX_StudentEnrollments_StudentId`, `IX_StudentEnrollments_AssignmentId`, and unique `UX_StudentEnrollments_StudentId_AssignmentId`.

## Implementation locations
- Models: `Models/Student.cs`, `Models/StudentPlan.cs`, `Models/StudentEnrollment.cs`
- EF entities: `Data/Entities/*Student*.cs`
- DbContext: `Data/AppDbContext.cs`
- In-memory/store model: `Services/DataStore.cs`
- SQLite persistence and initialization: `Services/SqliteStoreService.cs`
