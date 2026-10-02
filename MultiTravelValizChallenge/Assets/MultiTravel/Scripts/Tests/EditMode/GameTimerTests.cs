using MultiTravel.Core.Timing;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class GameTimerTests
    {
        private ManualClock clock;
        private GameTimer timer;
        private int started;
        private int stopped;

        [SetUp]
        public void SetUp()
        {
            clock = new ManualClock(100d);
            timer = new GameTimer(clock);
            started = 0;
            stopped = 0;
            timer.Started += () => started++;
            timer.Stopped += () => stopped++;
        }

        [Test]
        public void Initially_NotRunning_AndZero()
        {
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0, timer.ElapsedMs);
        }

        [Test]
        public void Start_ThenAdvance_ReportsElapsed()
        {
            timer.Start();
            clock.Advance(1.5);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(1500, timer.ElapsedMs);
            Assert.AreEqual(1, started);
        }

        [Test]
        public void Stop_FreezesValue()
        {
            timer.Start();
            clock.AdvanceMs(2345);
            timer.Stop();
            clock.Advance(10);

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(2345, timer.ElapsedMs);
            Assert.AreEqual(1, stopped);
        }

        [Test]
        public void Start_AfterStop_Resumes()
        {
            timer.Start();
            clock.Advance(1);
            timer.Stop();
            clock.Advance(5);
            timer.Start();
            clock.Advance(2);

            Assert.AreEqual(3000, timer.ElapsedMs);
            Assert.AreEqual(2, started);
        }

        [Test]
        public void Start_WhileRunning_IsNoop()
        {
            timer.Start();
            clock.Advance(1);
            timer.Start();
            clock.Advance(1);

            Assert.AreEqual(2000, timer.ElapsedMs);
            Assert.AreEqual(1, started);
        }

        [Test]
        public void Stop_WhileStopped_IsNoop()
        {
            timer.Stop();
            Assert.AreEqual(0, stopped);
        }

        [Test]
        public void Reset_StopsAndZeroes()
        {
            timer.Start();
            clock.Advance(4);
            timer.Reset();
            clock.Advance(4);

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0, timer.ElapsedMs);
            Assert.AreEqual(1, stopped, "Reset raises Stopped when it was running");

            timer.Reset();
            Assert.AreEqual(1, stopped, "Reset while stopped does not raise Stopped");
        }

        [Test]
        public void Reset_ThenStart_CountsFromZero()
        {
            timer.Start();
            clock.Advance(4);
            timer.Reset();
            timer.Start();
            clock.AdvanceMs(250);

            Assert.AreEqual(250, timer.ElapsedMs);
        }

        [Test]
        public void ElapsedMs_IsFloorOfMilliseconds()
        {
            timer.Start();
            clock.Advance(0.1);
            clock.Advance(0.2);
            Assert.AreEqual(300, timer.ElapsedMs);
        }
    }
}
