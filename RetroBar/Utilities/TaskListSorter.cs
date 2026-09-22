using ManagedShell.WindowsTasks;
using RetroBar.Controls;
using System.Collections;
using System.Collections.Generic;

namespace RetroBar.Utilities
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
            if (x is ApplicationWindow a && y is ApplicationWindow b)
            {
                List<string> desiredOrder = Settings.Instance.GetTaskOrderForEdge(_taskList.HostEdge);

                string idA = TaskAssignmentManager.GetIdentifier(a, TaskAssignmentMode.WindowClassAndTitle);
                string idB = TaskAssignmentManager.GetIdentifier(b, TaskAssignmentMode.WindowClassAndTitle);

                int indexA = idA != null ? desiredOrder.IndexOf(idA) : -1;
                int indexB = idB != null ? desiredOrder.IndexOf(idB) : -1;

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

            return 0;
        }
    }
}
