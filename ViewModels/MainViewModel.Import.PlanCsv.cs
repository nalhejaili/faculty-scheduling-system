using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ImportPlanCsv()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to import the plan.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                Title = "plan.csv (CourseId,SectionsRequested,PreferredFacultyId?,PreferredSlotId?,TheorySlotIds?,PracticalSlotIds?)"
            };
            if (ofd.ShowDialog() != true) return;

            var lines = File.ReadAllLines(ofd.FileName);
            if (lines.Length == 0) { MessageBox.Show("The plan file is empty.", "Notice"); return; }

            char sep = lines[0].Count(c => c == ';') > lines[0].Count(c => c == ',') ? ';' : ',';
            var header = lines[0].Split(sep).Select(h => h.Trim()).ToArray();

            int idxId = Array.FindIndex(header, h => h.Equals("CourseId", StringComparison.OrdinalIgnoreCase));
            int idxSec = Array.FindIndex(header, h => h.Equals("SectionsRequested", StringComparison.OrdinalIgnoreCase));
            int idxFid = Array.FindIndex(header, h => h.Equals("PreferredFacultyId", StringComparison.OrdinalIgnoreCase));
            int idxSid = Array.FindIndex(header, h => h.Equals("PreferredSlotId", StringComparison.OrdinalIgnoreCase));
            int idxTheory = Array.FindIndex(header, h => h.Equals("TheorySlotIds", StringComparison.OrdinalIgnoreCase));
            int idxPrac = Array.FindIndex(header, h => h.Equals("PracticalSlotIds", StringComparison.OrdinalIgnoreCase));
            if (idxId < 0 || idxSec < 0)
            {
                MessageBox.Show("Required headers: CourseId, SectionsRequested (+ PreferredFacultyId, PreferredSlotId, TheorySlotIds, PracticalSlotIds optional).", "Notice");
                return;
            }

            static System.Collections.Generic.List<int> ParseIds(string? cell)
            {
                if (string.IsNullOrWhiteSpace(cell)) return new();
                var parts = cell.Split(new[] { '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var ids = new System.Collections.Generic.List<int>();
                foreach (var p in parts)
                    if (int.TryParse(p.Trim(), out var id) && id > 0) ids.Add(id);
                return ids.Distinct().ToList();
            }

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(sep);
                if (parts.Length <= Math.Max(idxId, idxSec)) continue;

                if (!int.TryParse(parts[idxId].Trim(), out var cid)) continue;
                int sections = int.TryParse(parts[idxSec].Trim(), out var t) ? t : 0;

                int? fid = null; if (idxFid >= 0 && idxFid < parts.Length && int.TryParse(parts[idxFid].Trim(), out var tf)) fid = tf;
                int? sid = null; if (idxSid >= 0 && idxSid < parts.Length && int.TryParse(parts[idxSid].Trim(), out var ts)) sid = ts;

                var theoryIds = idxTheory >= 0 && idxTheory < parts.Length ? ParseIds(parts[idxTheory]) : null;
                var pracIds = idxPrac >= 0 && idxPrac < parts.Length ? ParseIds(parts[idxPrac]) : null;

                var row = PlanRows.FirstOrDefault(p => p.CourseId == cid);
                if (row != null)
                {
                    row.SectionsRequested = sections;
                    row.PreferredFacultyId = fid;
                    row.PreferredSlotId = sid;

                    if (theoryIds != null) row.TheorySlotIds = theoryIds;
                    if (pracIds != null) row.PracticalSlotIds = pracIds;
                }
            }

            RaiseAllCanExec();
            MessageBox.Show("The operational plan has been imported from CSV.", "Completed");
        }
    }
}
