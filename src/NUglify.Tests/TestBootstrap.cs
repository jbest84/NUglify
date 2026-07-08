using System.Text;
using NUnit.Framework;

namespace NUglify.Tests
{
    [SetUpFixture]
    public sealed class TestBootstrap
    {
        [OneTimeSetUp]
        public void RegisterEncodings()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
