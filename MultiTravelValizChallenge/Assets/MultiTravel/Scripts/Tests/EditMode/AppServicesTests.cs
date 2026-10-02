using System;
using MultiTravel.Core.Services;
using MultiTravel.Core.Timing;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class AppServicesTests
    {
        [SetUp]
        public void SetUp()
        {
            AppServices.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AppServices.Clear();
        }

        [Test]
        public void Register_ThenGet_ReturnsSameInstance()
        {
            var clock = new ManualClock();
            AppServices.Register<IClock>(clock);

            Assert.IsTrue(AppServices.IsRegistered<IClock>());
            Assert.AreSame(clock, AppServices.Get<IClock>());
            Assert.AreEqual(1, AppServices.Count);
        }

        [Test]
        public void Get_Unregistered_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => AppServices.Get<IClock>());
        }

        [Test]
        public void TryGet_ReturnsFalseWhenMissing_TrueWhenPresent()
        {
            Assert.IsFalse(AppServices.TryGet<IClock>(out var missing));
            Assert.IsNull(missing);

            var clock = new ManualClock();
            AppServices.Register<IClock>(clock);
            Assert.IsTrue(AppServices.TryGet<IClock>(out var found));
            Assert.AreSame(clock, found);
        }

        [Test]
        public void Register_Twice_ReplacesInstance()
        {
            var first = new ManualClock();
            var second = new ManualClock();
            AppServices.Register<IClock>(first);
            AppServices.Register<IClock>(second);

            Assert.AreSame(second, AppServices.Get<IClock>());
            Assert.AreEqual(1, AppServices.Count);
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AppServices.Register<IClock>(null));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            AppServices.Register<IClock>(new ManualClock());
            AppServices.Register(new ScoreServiceHolder());
            AppServices.Clear();

            Assert.AreEqual(0, AppServices.Count);
            Assert.IsFalse(AppServices.IsRegistered<IClock>());
        }

        [Test]
        public void Unregister_RemovesSingleService()
        {
            AppServices.Register<IClock>(new ManualClock());
            Assert.IsTrue(AppServices.Unregister<IClock>());
            Assert.IsFalse(AppServices.Unregister<IClock>());
            Assert.IsFalse(AppServices.IsRegistered<IClock>());
        }

        private sealed class ScoreServiceHolder
        {
        }
    }
}
