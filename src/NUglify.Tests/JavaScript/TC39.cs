using NUglify.Tests.JavaScript.Common;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    [TestFixture]
    public class TC39
    {
        [Test]
        public void ClassFields()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }
    }
}
