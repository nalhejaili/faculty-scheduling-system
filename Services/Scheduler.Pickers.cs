using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        private static bool AreConsecutive(Slot a, Slot b)
            => a.Day == b.Day && a.End == b.Start;

        private static List<Slot> PickSlotsTwoHours_OneBlockPerDay(
            IEnumerable<Slot> feasible, int unitSlotsNeeded, int blockSlots, int? preferredStartSlotId = null)
        {
            var ordered = (feasible as List<Slot>) ?? feasible.ToList();
            if (unitSlotsNeeded <= 0) return new List<Slot>();
            blockSlots = Math.Max(1, Math.Min(blockSlots, unitSlotsNeeded));

            var result = new List<Slot>(unitSlotsNeeded);
            var used = new HashSet<int>();

            int blocksNeeded = unitSlotsNeeded / blockSlots;
            int remainderUnits = unitSlotsNeeded % blockSlots;

            var byDay = ordered
                .GroupBy(s => DayOrder(s.Day))
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Start).ToList());

            var daysUsedForThisCourse = new HashSet<int>();

            if (preferredStartSlotId is int pref && ordered.Any(s => s.Id == pref))
            {
                var start = ordered.First(s => s.Id == pref);
                var dayKey = DayOrder(start.Day);
                var block = TryTakeConsecutiveBlockFromIndex(byDay[dayKey], start.Id, blockSlots, used);
                if (block.Count == blockSlots)
                {
                    foreach (var s in block) { result.Add(s); used.Add(s.Id); }
                    daysUsedForThisCourse.Add(dayKey);
                    blocksNeeded--;
                }
            }

            while (blocksNeeded > 0)
            {
                bool placedAny = false;
                foreach (var kv in byDay)
                {
                    if (blocksNeeded == 0) break;
                    var dayKey = kv.Key;
                    if (daysUsedForThisCourse.Contains(dayKey)) continue;

                    var list = kv.Value;
                    var block = TryTakeConsecutiveBlock(list, blockSlots, used);
                    if (block.Count == blockSlots)
                    {
                        foreach (var s in block) { result.Add(s); used.Add(s.Id); }
                        daysUsedForThisCourse.Add(dayKey);
                        blocksNeeded--;
                        placedAny = true;
                    }
                }
                if (!placedAny) break;
            }

            while (remainderUnits > 0)
            {
                var chosen = ordered.FirstOrDefault(s =>
                    !used.Contains(s.Id) &&
                    !daysUsedForThisCourse.Contains(DayOrder(s.Day)));
                if (chosen == null) break;
                result.Add(chosen);
                used.Add(chosen.Id);
                daysUsedForThisCourse.Add(DayOrder(chosen.Day));
                remainderUnits--;
            }

            while (blocksNeeded > 0)
            {
                foreach (var kv in byDay)
                {
                    if (blocksNeeded == 0) break;
                    var list = kv.Value;
                    var block = TryTakeConsecutiveBlock(list, blockSlots, used);
                    if (block.Count == blockSlots)
                    {
                        foreach (var s in block) { result.Add(s); used.Add(s.Id); }
                        blocksNeeded--;
                    }
                }
                if (blocksNeeded > 0) break;
            }

            while (remainderUnits > 0)
            {
                var s = ordered.FirstOrDefault(x => !used.Contains(x.Id));
                if (s == null) break;
                result.Add(s);
                used.Add(s.Id);
                remainderUnits--;
            }

            return result.Count == unitSlotsNeeded ? result : new List<Slot>();
        }

        private static List<Slot> TryTakeConsecutiveBlock(List<Slot> list, int blockSlots, HashSet<int> used)
        {
            for (int i = 0; i <= list.Count - blockSlots; i++)
            {
                var block = new List<Slot> { list[i] };
                if (used.Contains(list[i].Id)) continue;

                bool ok = true;
                for (int k = 1; k < blockSlots; k++)
                {
                    var next = list[i + k];
                    ok = !used.Contains(next.Id) && AreConsecutive(block[k - 1], next);
                    if (!ok) break;
                    block.Add(next);
                }
                if (ok) return block;
            }
            return new List<Slot>();
        }

        private static List<Slot> TryTakeConsecutiveBlockFromIndex(List<Slot> list, int startSlotId, int blockSlots, HashSet<int> used)
        {
            int idx = list.FindIndex(s => s.Id == startSlotId);
            if (idx < 0 || idx + blockSlots > list.Count) return new List<Slot>();

            var block = new List<Slot> { list[idx] };
            if (used.Contains(list[idx].Id)) return new List<Slot>();

            for (int k = 1; k < blockSlots; k++)
            {
                var next = list[idx + k];
                if (used.Contains(next.Id) || !AreConsecutive(block[k - 1], next))
                    return new List<Slot>();
                block.Add(next);
            }
            return block;
        }

        private static List<Slot> TryTakeFixedHourBlockAnyDay(IEnumerable<Slot> feasible, double hours, double minSlotHours)
        {
            int need = Math.Max(1, (int)Math.Ceiling(hours / minSlotHours));

            var byDayDesc = feasible
                .GroupBy(s => s.Day)
                .OrderByDescending(g => DayOrder(g.Key))
                .Select(g => g.OrderBy(s => s.Start).ToList());

            foreach (var daySlots in byDayDesc)
            {
                for (int i = 0; i <= daySlots.Count - need; i++)
                {
                    var block = new List<Slot> { daySlots[i] };
                    bool ok = true;
                    for (int k = 1; k < need; k++)
                    {
                        var nxt = daySlots[i + k];
                        if (!AreConsecutive(block[k - 1], nxt)) { ok = false; break; }
                        block.Add(nxt);
                    }
                    if (ok) return block;
                }
            }
            return new List<Slot>();
        }

        private static List<Slot> TryTakeSinglesLatestFirst(IEnumerable<Slot> feasible, int unitsNeeded)
        {
            var list = feasible
                .OrderByDescending(s => DayOrder(s.Day))
                .ThenByDescending(s => s.Start)
                .ToList();

            var chosen = new List<Slot>(unitsNeeded);
            var used = new HashSet<int>();

            foreach (var s in list)
            {
                if (used.Add(s.Id))
                {
                    chosen.Add(s);
                    if (chosen.Count == unitsNeeded) break;
                }
            }
            return (chosen.Count == unitsNeeded) ? chosen : new List<Slot>();
        }
    }
}
