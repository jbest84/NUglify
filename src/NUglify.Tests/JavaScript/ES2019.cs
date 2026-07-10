using NUglify.Tests.JavaScript.Common;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    [TestFixture]
    public class ES2019
    {

        [Test]
        public void OptionalCatchBinding()
        {
            TestHelper.Instance.RunTest();
        }
    }
}
