using System.Threading.Tasks;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary>
    /// Reads the outcome of a task that must already be complete. Every fake in this folder completes synchronously,
    /// so Core async code never needs a <c>UnitySynchronizationContext</c> pump in EditMode tests. If a task is still
    /// pending the test fails immediately instead of blocking (never call <c>.Result</c> / <c>.Wait()</c> on a pending
    /// task: a synchronous test runner would deadlock the Editor main thread).
    /// </summary>
    public static class SyncTask
    {
        public static T Completed<T>(Task<T> task)
        {
            Assert.IsNotNull(task, "task");
            Assert.IsTrue(task.IsCompleted, "Task was expected to complete synchronously with the test fakes (status: " + task.Status + ").");
            return task.GetAwaiter().GetResult();
        }

        public static void Completed(Task task)
        {
            Assert.IsNotNull(task, "task");
            Assert.IsTrue(task.IsCompleted, "Task was expected to complete synchronously with the test fakes (status: " + task.Status + ").");
            task.GetAwaiter().GetResult();
        }
    }
}
