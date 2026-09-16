using Microsoft.EntityFrameworkCore;
using TrainerScheduler.Data.Entities;

namespace TrainerScheduler.Data
{
    /// <summary>
    /// SQLite local database for TrainerScheduler.
    /// </summary>
    public sealed class AppDbContext : DbContext
    {
        public DbSet<DepartmentEntity> Departments => Set<DepartmentEntity>();
        public DbSet<FacultyEntity> Faculties => Set<FacultyEntity>();
        public DbSet<CourseEntity> Courses => Set<CourseEntity>();
        public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
        public DbSet<SlotEntity> Slots => Set<SlotEntity>();
        public DbSet<UserEntity> Users => Set<UserEntity>();
        public DbSet<AssignmentEntity> Assignments => Set<AssignmentEntity>();
        public DbSet<FacultySlotBlockEntity> FacultySlotBlocks => Set<FacultySlotBlockEntity>();
        public DbSet<ServerMetaEntity> ServerMeta => Set<ServerMetaEntity>();

        public DbSet<StudentEntity> Students => Set<StudentEntity>();
        public DbSet<StudentPlanEntity> StudentPlans => Set<StudentPlanEntity>();
        public DbSet<StudentEnrollmentEntity> StudentEnrollments => Set<StudentEnrollmentEntity>();
        public DbSet<StudentUnassignedEntity> StudentUnassigned => Set<StudentUnassignedEntity>();

        public DbSet<CourseCodeMapEntity> CourseCodeMaps => Set<CourseCodeMapEntity>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DepartmentEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<FacultyEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<CourseEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<RoomEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<SlotEntity>().HasKey(x => x.Id);

            // Students (no FK constraints; keep robust)
            modelBuilder.Entity<StudentEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<StudentEntity>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<StudentEntity>().HasIndex(x => x.StudentNo);
            modelBuilder.Entity<StudentEntity>().HasIndex(x => x.DepartmentId);

            modelBuilder.Entity<StudentPlanEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<StudentPlanEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<StudentPlanEntity>().HasIndex(x => x.StudentId);
            modelBuilder.Entity<StudentPlanEntity>().HasIndex(x => x.CourseId);

            modelBuilder.Entity<StudentEnrollmentEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<StudentEnrollmentEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<StudentEnrollmentEntity>().HasIndex(x => x.StudentId);
            modelBuilder.Entity<StudentEnrollmentEntity>().HasIndex(x => x.AssignmentId);
            modelBuilder.Entity<StudentEnrollmentEntity>().HasIndex(x => new { x.StudentId, x.AssignmentId }).IsUnique();

            modelBuilder.Entity<StudentUnassignedEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<StudentUnassignedEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<StudentUnassignedEntity>().ToTable("StudentUnassigned");
            modelBuilder.Entity<StudentUnassignedEntity>().Property(x => x.Kind).HasMaxLength(32);
            modelBuilder.Entity<StudentUnassignedEntity>().Property(x => x.Reason).HasMaxLength(256);
            modelBuilder.Entity<StudentUnassignedEntity>().HasIndex(x => x.StudentId);
            modelBuilder.Entity<StudentUnassignedEntity>().HasIndex(x => x.CourseId);
            modelBuilder.Entity<StudentUnassignedEntity>().HasIndex(x => x.Reason);


            // Course code mapping (PK = CourseCode)
            modelBuilder.Entity<CourseCodeMapEntity>().HasKey(x => x.CourseCode);
            modelBuilder.Entity<CourseCodeMapEntity>().Property(x => x.CourseCode).IsRequired();
            modelBuilder.Entity<CourseCodeMapEntity>().HasIndex(x => x.CourseId);

            modelBuilder.Entity<UserEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<UserEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<UserEntity>().HasIndex(x => x.Username).IsUnique();

            modelBuilder.Entity<AssignmentEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<AssignmentEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AssignmentEntity>().HasIndex(x => x.DepartmentId);
            modelBuilder.Entity<AssignmentEntity>().HasIndex(x => x.SlotId);
            modelBuilder.Entity<AssignmentEntity>().HasIndex(x => x.FacultyId);

            modelBuilder.Entity<FacultySlotBlockEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<FacultySlotBlockEntity>().Property(x => x.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<FacultySlotBlockEntity>().HasIndex(x => x.FacultyId);
            modelBuilder.Entity<FacultySlotBlockEntity>().HasIndex(x => x.SlotId);


            modelBuilder.Entity<ServerMetaEntity>().HasKey(x => x.Id);
            modelBuilder.Entity<ServerMetaEntity>().Property(x => x.Id).ValueGeneratedNever();

            modelBuilder.Entity<DepartmentEntity>().HasIndex(x => x.Name);
            modelBuilder.Entity<FacultyEntity>().HasIndex(x => x.DepartmentId);
            modelBuilder.Entity<CourseEntity>().HasIndex(x => x.DepartmentId);
            modelBuilder.Entity<CourseEntity>().HasIndex(x => x.Level);
            modelBuilder.Entity<CourseEntity>().HasIndex(x => x.CourseCode);
            modelBuilder.Entity<RoomEntity>().HasIndex(x => x.DepartmentId);
            modelBuilder.Entity<SlotEntity>().HasIndex(x => x.Day);
        }
    }
}
