using ManagedShell.WindowsTasks;
using UltraWinBar.Controls;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UltraWinBar.Utilities
{
    public class TaskListSorter : IComparer
    {
        private readonly TaskList _taskList;

        public TaskListSorter(TaskList taskList)
        {
            _taskList = taskList;
        }

        public int Compare(object x, object y)
        {
            // A window can close mid-sort; ApplicationWindow property access on a dead hWnd
            // can throw. An exception here would corrupt ListCollectionView's sort state and
            // could leave the task list stuck empty, so never let one escape — worst case,
            // this pair just doesn't get ordered this pass.
            try
            {
                if (x is ApplicationWindow a && y is ApplicationWindow b)
                {
                    // Cached ranks: O(1) per comparison instead of rebuilding and scanning the order list.
                    IReadOnlyDictionary<string, int> ranks = Settings.Instance.GetTaskOrderRanks(_taskList.HostEdge);

                    string idA = TaskOrderIdentifier.Get(a, _taskList.Tasks);
                    string idB = TaskOrderIdentifier.Get(b, _taskList.Tasks);

                    int indexA = idA != null && ranks.TryGetValue(idA, out int rankA) ? rankA : -1;
                    int indexB = idB != null && ranks.TryGetValue(idB, out int rankB) ? rankB : -1;

                    if (indexA < 0 && indexB < 0)
                    {
                        return 0;
                    }

                    if (indexA < 0)
                    {
                        return 1;
                    }

                    if (indexB < 0)
                    {
                        return -1;
                    }

                    return indexA.CompareTo(indexB);
                }
            }
            catch (Exception)
            {
                return 0;
            }

            return 0;
        }
    }
}
