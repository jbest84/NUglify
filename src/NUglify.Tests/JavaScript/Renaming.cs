using NUglify.Tests.JavaScript.Common;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    [TestFixture]
    public class Renaming
    {
        [Test]
        public void NestedGlobals()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }

        [Test]
        public void ManualRename()
        {
            TestHelper.Instance.RunTest("-enc:out ascii");
        }

        [Test]
        public void ManualRename_cmd()
        {
            TestHelper.Instance.RunTest("-rename:globalFunction=_g,oneGlobal=g1,oneLocal=l1 -rename:oneParam=p1,twoParam=p2,nameOne=n1,你好=中文,while=for -enc:out ascii");
        }

        [Test]
        public void ManualRename_rename()
        {
            TestHelper.Instance.RunTest("-rename Rename.xml -enc:out ascii");
        }

        [Test]
        public void ManualRename_noprops()
        {
            TestHelper.Instance.RunTest("-rename Rename.xml -rename:noprops -enc:out ascii");
        }

        [Test]
        public void ManualRename_all()
        {
            TestHelper.Instance.RunTest("-rename:all -enc:out ascii");
        }

        [Test]
        public void ManualRename_collide()
        {
            TestHelper.Instance.RunTest("-rename:all -rename Collide.xml");
        }

        [Test]
        public void ManualRename_norename()
        {
            TestHelper.Instance.RunTest("-rename:all -rename NoRename.xml -enc:out ascii");
        }

        [Test]
        public void Super()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }

        [Test]
        public void NoMunge()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }

        [Test]
        public void IfFunction()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }

        [Test]
        public void SafeAll()
        {
            TestHelper.Instance.RunTest("-rename:all -evals:safeall");
        }

        [Test]
        public void SafeAll_imm()
        {
            TestHelper.Instance.RunTest("-rename:all -evals:immediate");
        }

        [Test]
        public void LabelKeyword()
        {
            // ignore the unused-label warning
            TestHelper.Instance.RunErrorTest("-ignore:JS1021");
        }

        [Test]
        public void evalImmediate()
        {
            // ignore the unused-label warning
            TestHelper.Instance.RunErrorTest("-evals:immediate");
        }

        [Test]
        public void AutoRenameGlobals()
        {
            TestHelper.Instance.RunErrorTest("-rename:all -autoglobals");
        }
    }
}
