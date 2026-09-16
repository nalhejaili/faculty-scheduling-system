using System;
using System.Collections.Generic;

namespace MiniTrainerScheduler.Services
{
    /// <summary>
    /// Schema (header mapping) for the "Expected Registration" CSV report.
    /// </summary>
    public sealed class ExpectedRegistrationCsvSchema
    {
        public string FilePath { get; }
        public char Delimiter { get; }

        public IReadOnlyList<string> RawHeaders { get; }
        public IReadOnlyList<string> NormalizedHeaders { get; }

        /// <summary>
        /// Map: normalized header -> column index.
        /// </summary>
        public IReadOnlyDictionary<string, int> IndexByHeader { get; }

        // ===== Required columns (normalized keys)
        public int StudentNoIndex { get; }
        public int StudentNameIndex { get; }
        public int DepartmentIndex { get; }
        public int SpecialtyIndex { get; }
        public int TermIndex { get; }
        public int CourseCodeIndex { get; }
        public int CourseNameIndex { get; }
        public int ScheduleKindIndex { get; }

        // ===== Optional columns
        public int? TraineeStatusIndex { get; }
        public int? RegistrationStatusIndex { get; }
        public int? ContactHoursIndex { get; }
        public int? CreditHoursIndex { get; }

        public ExpectedRegistrationCsvSchema(
            string filePath,
            char delimiter,
            IReadOnlyList<string> rawHeaders,
            IReadOnlyList<string> normalizedHeaders,
            IReadOnlyDictionary<string, int> indexByHeader,
            int studentNoIndex,
            int studentNameIndex,
            int departmentIndex,
            int specialtyIndex,
            int termIndex,
            int courseCodeIndex,
            int courseNameIndex,
            int scheduleKindIndex,
            int? traineeStatusIndex,
            int? registrationStatusIndex,
            int? contactHoursIndex,
            int? creditHoursIndex)
        {
            FilePath = filePath;
            Delimiter = delimiter;
            RawHeaders = rawHeaders;
            NormalizedHeaders = normalizedHeaders;
            IndexByHeader = indexByHeader;

            StudentNoIndex = studentNoIndex;
            StudentNameIndex = studentNameIndex;
            DepartmentIndex = departmentIndex;
            SpecialtyIndex = specialtyIndex;
            TermIndex = termIndex;
            CourseCodeIndex = courseCodeIndex;
            CourseNameIndex = courseNameIndex;
            ScheduleKindIndex = scheduleKindIndex;

            TraineeStatusIndex = traineeStatusIndex;
            RegistrationStatusIndex = registrationStatusIndex;
            ContactHoursIndex = contactHoursIndex;
            CreditHoursIndex = creditHoursIndex;
        }
    }
}
