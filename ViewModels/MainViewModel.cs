using Microsoft.Win32;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using System.Collections.Generic;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// </summary>
    public sealed partial class MainViewModel : INotifyPropertyChanged
    {
        public DataStore Store { get; } = new();

        public ObservableCollection<Department> Departments { get; } = new();
        public ObservableCollection<int> Levels { get; } = new();

        public ObservableCollection<CoursePlanRow> PlanRows { get; } = new();

        public ObservableCollection<QueuedPlanRow> QueuedPlan { get; } = new();

        public ObservableCollection<AssignmentRow> Rows { get; } = new();

        public ObservableCollection<Faculty> Faculties { get; } = new();
        public ObservableCollection<Course> Courses { get; } = new();
        public ObservableCollection<Slot> Slots { get; } = new();
        public ObservableCollection<Faculty> FacultiesInSelectedDepartment { get; } = new();

        public ObservableCollection<FacultyDeptOverrideRow> FdoRows { get; } = new();
        public ObservableCollection<CourseFacultyOverrideRow> CfoRows { get; } = new();

        public Faculty? SelectedOverrideFaculty { get; set; }
        public Department? SelectedOverrideDepartment { get; set; }
        public Course? SelectedOverrideCourse { get; set; }
        public FacultyDeptOverrideRow? SelectedFdoRow { get; set; }
        public CourseFacultyOverrideRow? SelectedCfoRow { get; set; }

        public ObservableCollection<FacultyLoadOverrideRow> FloRows { get; } = new();
        public FacultyLoadOverrideRow? SelectedFloRow { get; set; }
        public int? SelectedFloHours { get; set; } = 6;
        public ObservableCollection<Room> Rooms { get; } = new();

        public ICommand AddFloCommand { get; }
        public ICommand RemoveFloCommand { get; }
        private int? _manualSelectedRoomId;
        public int? ManualSelectedRoomId
        {
            get => _manualSelectedRoomId;
            set { _manualSelectedRoomId = value; OnPropertyChanged(); }
        }

        public ObservableCollection<int> ManualLevels { get; } = new();
        public ObservableCollection<Course> ManualCourses { get; } = new();
        public ObservableCollection<Faculty> ManualDeptFaculties { get; } = new();
        private Department? _manualSelectedDepartment;
        public Department? ManualSelectedDepartment
        {
            get => _manualSelectedDepartment;
            set
            {
                value = CoerceDepartmentForSupervisor(value);

                _manualSelectedDepartment = value;
                OnPropertyChanged();
                RefreshManualLevels();
                RefreshManualCourses();
                RefreshManualFaculties();
                RaiseAllCanExec();
            }
        }
        private int? _manualSelectedLevel;
        public int? ManualSelectedLevel
        {
            get => _manualSelectedLevel;
            set { _manualSelectedLevel = value; OnPropertyChanged(); RefreshManualCourses(); RaiseAllCanExec(); }
        }
        public Course? ManualSelectedCourse { get; set; }
        public Faculty? ManualSelectedFaculty { get; set; }
        public int? ManualSelectedSlotId { get; set; }
        private int _manualSections = 1;
        public int ManualSections { get => _manualSections; set { _manualSections = value < 0 ? 0 : value; OnPropertyChanged(); RaiseAllCanExec(); } }

        public Array ScheduleKinds { get; } = Enum.GetValues(typeof(ScheduleKind));
        private ScheduleKind _selectedKind = ScheduleKind.Training;
        public ScheduleKind SelectedKind { get => _selectedKind; set { _selectedKind = value; OnPropertyChanged(); } }

        private Department? _selectedDepartment;
        public Department? SelectedDepartment
        {
            get => _selectedDepartment;
            set
            {
                value = CoerceDepartmentForSupervisor(value);

                _selectedDepartment = value;
                OnPropertyChanged();
                RefreshLevels();
                RefreshDeptFaculties();
                RefreshPlanRows();
                RaiseAllCanExec();
            }
        }
        private int? _selectedLevel;
        public int? SelectedLevel
        {
            get => _selectedLevel;
            set { _selectedLevel = value; OnPropertyChanged(); RefreshPlanRows(); RaiseAllCanExec(); }
        }

        public ICommand GenerateCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportCsvByDeptCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ImportMasterCsvCommand { get; }
        public ICommand ImportPlanCsvCommand { get; }
        public ICommand ImportJsonCommand { get; }

        public ICommand SaveQueuedPlanJsonCommand { get; }
        public ICommand LoadQueuedPlanJsonCommand { get; }
        public ICommand AddFdoCommand { get; }
        public ICommand RemoveFdoCommand { get; }
        public ICommand AddCfoCommand { get; }
        public ICommand RemoveCfoCommand { get; }

        public ICommand AddCurrentDeptToPlanCommand { get; }
        public ICommand ClearQueuedPlanCommand { get; }
        public ICommand RemoveQueuedItemCommand { get; }
        public ICommand AddManualToQueuedPlanCommand { get; }

        public MainViewModel()
        {
            GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => CanGenerate());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Rows.Any());
            ExportCsvByDeptCommand = new RelayCommand(_ => ExportCsvByDept(), _ => Rows.Any());
            ImportStudentsJsonCommand = new RelayCommand(async _ => await ImportStudentsJsonAsync(), _ => AuthContext.IsAdmin);
            ImportStudentPlansJsonCommand = new RelayCommand(async _ => await ImportStudentPlansJsonAsync(), _ => AuthContext.IsAdmin);
            ExportStudentsJsonCommand = new RelayCommand(_ => ExportStudentsJson(), _ => AuthContext.IsAdmin && Store != null && Store.Students.Any());
            ExportStudentEnrollmentsJsonCommand = new RelayCommand(_ => ExportStudentEnrollmentsJson(), _ => AuthContext.IsAdmin && Store != null && Store.StudentEnrollments.Any());
            DistributeStudentsCommand = new RelayCommand(async _ => await DistributeStudentsAsync(), _ => AuthContext.IsAdmin && Store != null && Store.Students.Any() && Store.StudentPlans.Any() && Store.Assignments.Any());

            ValidateExpectedRegistrationCsvCommand = new RelayCommand(async _ => await ValidateExpectedRegistrationCsvAsync(), _ => AuthContext.IsAdmin);
            ImportExpectedRegistrationCsvCommand = new RelayCommand(async _ => await ImportExpectedRegistrationCsvAsync(), _ => AuthContext.IsAdmin);

            BuildQueuedPlanFromExpectedRegistrationCommand = new RelayCommand(
                _ => BuildQueuedPlanFromExpectedRegistration(),
                _ => !IsBusy && (IsAdmin || IsSupervisor) );

            ClearCommand = new RelayCommand(_ =>
            {
                ClearGeneratedOnly();
            });

            ImportMasterCsvCommand = new RelayCommand(_ => ImportMasterCsv(), _ => IsAdmin);
            ImportPlanCsvCommand = new RelayCommand(_ => ImportPlanCsv(), _ => IsAdmin);
            ImportJsonCommand = new RelayCommand(_ => ImportJson(), _ => IsAdmin);

            SaveQueuedPlanJsonCommand = new RelayCommand(_ => SaveQueuedPlanJson(), _ => QueuedPlan.Any());
            LoadQueuedPlanJsonCommand = new RelayCommand(_ => LoadQueuedPlanJson());

            AddFdoCommand = new RelayCommand(_ => AddFdo(), _ => IsAdmin);
            RemoveFdoCommand = new RelayCommand(_ => RemoveFdo(), _ => IsAdmin);
            AddCfoCommand = new RelayCommand(_ => AddCfo(), _ => IsAdminOrSupervisor);
            RemoveCfoCommand = new RelayCommand(_ => RemoveCfo(), _ => IsAdminOrSupervisor);

            AddCurrentDeptToPlanCommand = new RelayCommand(_ => AddCurrentDeptToPlan(), _ => CanAddCurrentDept());
            ClearQueuedPlanCommand = new RelayCommand(_ => { QueuedPlan.Clear(); RaiseAllCanExec(); });
            RemoveQueuedItemCommand = new RelayCommand(it => { if (it is QueuedPlanRow q) QueuedPlan.Remove(q); RaiseAllCanExec(); });
            AddManualToQueuedPlanCommand = new RelayCommand(_ => AddManualToQueuedPlan(), _ => CanAddManual());

            

            AddSectionOverrideCommand = new RelayCommand(
                it => { if (it is CoursePlanRow r) AddSectionOverride(r); RaiseAllCanExec(); },
                it => it is CoursePlanRow);

            RemoveSectionOverrideCommand = new RelayCommand(
                it => { if (it is CoursePlanRow r) RemoveSectionOverride(r); RaiseAllCanExec(); },
                it => it is CoursePlanRow r && r.SectionIndex is not null);

            PlanRows.CollectionChanged += PlanRows_CollectionChanged;

            AddFloCommand = new RelayCommand(_ => AddFlo(), _ => IsAdmin);
            RemoveFloCommand = new RelayCommand(_ => RemoveFlo(), _ => IsAdmin);
            ManualPlanBridge.ManualAssignmentRegistered += OnManualAssignmentRegistered;

            InitManualAssignmentListener();

            InitLanStatusOnStartup();

            InitStudentScheduleCommands();

            InitStudentWeeklyViewCommands();

        }

        private void OnManualAssignmentRegistered(Assignment a)
        {
            if (Store is null) return;

            var course = Store.Courses.FirstOrDefault(c => c.Id == a.CourseId);
            if (course is null) return;

            var existing = QueuedPlan.FirstOrDefault(q => q.CourseId == a.CourseId);
            if (existing != null)
            {
                if (a.FacultyId > 0)
                    existing.PreferredFacultyId = a.FacultyId;

                if (a.SlotId > 0)
                    existing.PreferredSlotId = a.SlotId;

                RaiseAllCanExec();
                return;
            }

            var dept = Store.Departments.FirstOrDefault(d => d.Id == a.DepartmentId);
            var deptName = dept?.Name ?? "";

            int level = course.Level;

            var (th, lab, _) = SectionPlanner.GetHoursSplit(Store, course);

            QueuedPlan.Add(new QueuedPlanRow
            {
                DepartmentId = a.DepartmentId,
                DepartmentName = deptName,
                Level = level,

                CourseId = course.Id,
                CourseName = course.Name,

                TheoryHoursRequired = th,
                PracticalHoursRequired = lab,

                PreferredFacultyId = a.FacultyId,
                PreferredSlotId = a.SlotId,

                SectionsRequested = 1
            });

            RaiseAllCanExec();
        }
















































        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

       

       

      

  


        public QueuedPlanRow? SelectedQueuedPlanRow { get; set; }



       
    }
}
