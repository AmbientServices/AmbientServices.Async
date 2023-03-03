using AmbientServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AmbientServices.Test
{
    [TestClass]
    public class TestAmbientService
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext context)
        {
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            System.Threading.Tasks.ValueTask t = TraceBuffer.Flush();
            t.GetAwaiter().GetResult();
            FifoTaskScheduler.Stop();
        }
    }
}
