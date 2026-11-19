// Modules.cs
//
// Copyright 2013 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using NUglify.JavaScript;
using NUglify.Tests.JavaScript.Common;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    [TestFixture]
    public class Modules
    {
        [Test]
        public void Header()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void NotModules()
        {
            TestHelper.Instance.RunErrorTest("-unused:keep -ignore:JS1310,JS1135");
        }

        [Test]
        public void Module()
        {
            TestHelper.Instance.RunErrorTest("-unused:keep");
        }

        [Test]
        public void Imports()
        {
            TestHelper.Instance.RunErrorTest("-unused:keep");
        }

        [Test]
        public void ImportRename()
        {
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void Exports()
        {
            TestHelper.Instance.RunErrorTest("-unused:keep");
        }

        [Test]
        public void ExportRename()
        {
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void ExportCombine()
        {
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void NoExportInline()
        {
            // if we analyze the exports of the inline module, we should see that the expected import isn't actually exported
            TestHelper.Instance.RunErrorTest(/*JSError.NoModuleExport*/);
        }

        [Test]
        public void NoExportExternal()
        {
            // if we load the module and check, we should see that the expected import isn't actually exported
            TestHelper.Instance.RunErrorTest(/*JSError.NoModuleExport*/);
        }

        [Test]
        public void ReExport()
        {
            TestHelper.Instance.RunErrorTest(JSError.UndeclaredFunction, JSError.UndeclaredVariable);
        }

        [Test]
        public void BadExport()
        {
            TestHelper.Instance.RunErrorTest(JSError.ExportNotAtModuleLevel);
        }

        [TestCase("export default ;", false)]
        [TestCase("export default ;", true)]
        [TestCase("export default var x = null;", false)]
        [TestCase("export default var x = null;", true)]
        [TestCase("export default var x = null; export default var x = null;", false)]
        [TestCase("export default var x = null; export default var x = null;", true)]
        public void MalformedDefaultExportsReportErrors(string source, bool minify)
        {
            var result = Uglify.Js(source, new CodeSettings
            {
                SourceMode = JavaScriptSourceMode.Module,
                MinifyCode = minify
            });
            Assert.That(result.HasErrors, Is.True, "Malformed exports must report syntax errors without triggering a Debug assertion.");
        }

        [Test]
        public void ImportAssign()
        {
            TestHelper.Instance.RunErrorTest(JSError.AssignmentToConstant);
        }

        //[Test]
        //public void NoExportExternal_flatten()
        //{
        //    TestHelper.Instance.RunErrorTest("-imports:flatten", JSError.NoModuleExport);
        //}

        //[Test]
        //public void CircularExternal()
        //{
        //    TestHelper.Instance.RunErrorTest("-imports:flatten");
        //}
    }
}
