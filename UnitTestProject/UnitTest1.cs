using Microsoft.VisualStudio.TestTools.UnitTesting;
using rsyslogd;
using System;

namespace UnitTestProject {
    [TestClass]
    public class UnitTest1 {
        [TestMethod]
        public void TestMethod1() {
            string tst = "<182>Feb 28 17:42:20.513 axis-b8a44fd2a2c6 parhand[594]: Got SIGUSR1, reinitializing authorization data.\r\n";
            string r = Syslog.DecodePacket(tst);
            Console.WriteLine(r);
        }
    }
}
