# Expected Registration CSV Mapping

## Purpose
Each CSV row represents one student and one course within an academic term. A student may appear on multiple rows when registered for multiple courses.

## Required columns
The importer trims and normalizes header names before mapping them.

| CSV column | Internal use |
|---|---|
| `Student Number` | Student key (`StudentNo`) |
| `Student Name` | Full student name |
| `Department` | Department name |
| `Specialty` | Specialty/program name |
| `Academic Term` | `TermKey` |
| `Course Code` | Official course reference |
| `Course Name` | Course name |
| `Schedule Type` | Component kind (`THEORY`, `LAB`, `BLENDED`, `COOP`) |

## Optional columns
| CSV column | Internal use |
|---|---|
| `Student Status` | Filtering/reporting |
| `Registration Status` | Filtering/reporting |
| `Contact Hours` | Optional contact-hour value |
| `Credit Hours` | Optional credit-hour value |

## Import behavior
1. Read the header, delimiter, and encoding.
2. Normalize column names and validate required fields.
3. Create or update students by student number.
4. Build each student's expected course plan.
5. De-duplicate repeated course rows when lecture/lab rows represent the same course.
6. Map imported course codes to application courses using saved mappings, numeric IDs, or unique course-name matches.
7. Record unresolved course codes for review in Data Management.

The codebase retains legacy Arabic CSV compatibility internally through Unicode escape sequences, so the public source remains English-only while existing imports continue to work.
