using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// Checks that the registry does not keep pools alive. This needs a garbage collector that
    /// reliably collects an unreachable object on request, which the .NET runtime does and Unity's
    /// conservative collector does not promise, so the test lives outside the shared test folder.
    /// </summary>
    public class PoolRegistryCollectionTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterTemporaryPool(Localizer localizer)
        {
            string[] pool = localizer.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });
            return new WeakReference(pool);
        }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        [Test]
        public void Registry_DoesNotKeepAPoolAlive()
        {
            Localizer localizer = Fixtures.Localizer();
            WeakReference pool = RegisterTemporaryPool(localizer);
            string[] kept = localizer.LocalizePool("Tips", "short", new[] { "First", "Second" });

            Collect();

            Assert.That(pool.IsAlive, Is.False, "the registry must only hold a weak reference");
            Assert.That(localizer.Pools.Count, Is.EqualTo(1));
            GC.KeepAlive(kept);
        }

        [Test]
        public void LanguageChange_AfterAPoolWasCollected_StillWorks()
        {
            Localizer localizer = Fixtures.Localizer();
            RegisterTemporaryPool(localizer);
            string[] kept = localizer.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });

            Collect();
            localizer.SetLocale("de");

            Assert.That(kept, Is.EqualTo(new[] { "Tipp eins", "Tipp zwei", "Tipp drei" }));
            Assert.That(localizer.Pools.Count, Is.EqualTo(1));
        }
    }
}
