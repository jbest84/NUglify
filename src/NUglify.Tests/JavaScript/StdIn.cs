using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    /// <summary>
    /// Summary description for StdIn
    /// </summary>
    [TestFixture]
    public class StdIn
    {
        public StdIn()
        {
        }

        public static string ExpectedFolder { get; private set; }
        public static string InputFolder { get; private set; }
        public static string OutputFolder { get; private set; }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        [SetUp]
        public void Setup()
        {
            var dataFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"TestData\JS");
            var className = TestContext.CurrentContext.Test.ClassName.Substring(TestContext.CurrentContext.Test.ClassName.LastIndexOf('.') + 1);

            ExpectedFolder = Path.Combine(Path.Combine(dataFolder, "Expected"), className);
            InputFolder = Path.Combine(Path.Combine(dataFolder, "Input"), className);
            OutputFolder = Path.Combine(Path.Combine(dataFolder, "Output"), className);

            // output folder may not exist -- create it if it doesn't
            if (!Directory.Exists(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
            }
        }

        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        [Test]
        public void StdInTest()
        {
            var ndxUnderscore = TestContext.CurrentContext.Test.Name.IndexOf('_');
            var testName = ndxUnderscore < 0 ? TestContext.CurrentContext.Test.Name : TestContext.CurrentContext.Test.Name.Substring(0, ndxUnderscore);

            // get the input, output, and expected path names
            var inputPath = Path.ChangeExtension(Path.Combine(InputFolder, testName), ".js");
            var outputPath = Path.ChangeExtension(Path.Combine(OutputFolder, testName), ".js");
            var expectedPath = Path.ChangeExtension(Path.Combine(ExpectedFolder, testName), ".js");

            // get the input code
            string inputCode;
            using (var inputStream = new StreamReader(inputPath, true))
            {
                inputCode = inputStream.ReadToEnd();
            }

            // get the path to the NUglify assembly.
            // we are linking to the EXE in this project, so this gives us the path
            // we need to spawn a new process for which we can redirect the stdin stream.
            var ajaxMin = Assembly.GetAssembly(typeof(IScopeReport));
            var ajaxMinPath = ajaxMin.Location;
            var ajaxMinExePath = Path.ChangeExtension(ajaxMinPath, ".exe");

            // create the process to the EXE with a redirected stdin
            var ajaxMinProcess = new Process();
            ajaxMinProcess.StartInfo.ErrorDialog = false;
            ajaxMinProcess.StartInfo.UseShellExecute = false;
            ajaxMinProcess.StartInfo.RedirectStandardInput = true;
            ajaxMinProcess.StartInfo.RedirectStandardOutput = false;
            ajaxMinProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            // no input files mean pull from stdin. Add the -js flag so we know to expect JavaScript.
            // quote the output path because it might contains spaces.
            var arguments = "-a -js -out \"" + outputPath + '"';
            if (File.Exists(ajaxMinExePath))
            {
                ajaxMinProcess.StartInfo.FileName = ajaxMinExePath;
                ajaxMinProcess.StartInfo.Arguments = arguments;
            }
            else
            {
                ajaxMinProcess.StartInfo.FileName = "dotnet";
                ajaxMinProcess.StartInfo.Arguments = "\"" + ajaxMinPath + "\" " + arguments;
            }

            // trace the command line
            Trace.WriteLine("EXECUTING:");
            Trace.Write('"');
            Trace.Write(ajaxMinProcess.StartInfo.FileName);
            Trace.Write("\" ");
            Trace.WriteLine(ajaxMinProcess.StartInfo.Arguments);
            Trace.WriteLine(string.Empty);

            // trace the input path and code
            Trace.Write("INPUT CODE: ");
            Trace.WriteLine(inputPath);
            Trace.WriteLine(inputCode);
            Trace.WriteLine(string.Empty);

            // for debugging purposes
            Trace.WriteLine(string.Format("odd \"{0}\" \"{1}\"", expectedPath, outputPath));
            Trace.WriteLine(string.Empty);

            // start the process
            ajaxMinProcess.Start();

            // write the input file to the redirected standard input, and close the standard input
            // to signal that we're done
            ajaxMinProcess.StandardInput.Write(inputCode);
            ajaxMinProcess.StandardInput.Close();

            // and wait for the process to exit -- but no more than 10 seconds
            ajaxMinProcess.WaitForExit(10000);

            // if it hasn't exited normally, KILL IT with extreme predjudice.
            // (protect against an INFINITE LOOP or something)
            if (!ajaxMinProcess.HasExited)
            {
                ajaxMinProcess.Kill();
                Assert.Fail("process had to be killed - infinite loop?");
            }

            Trace.Write("EXIT CODE: ");
            Trace.WriteLine(ajaxMinProcess.ExitCode.ToString("X"));
            Trace.WriteLine(string.Empty);

            // no longer need the process
            ajaxMinProcess.Close();

            // read the expected code
            string expectedCode;
            using (var reader = new StreamReader(expectedPath))
            {
                expectedCode = reader.ReadToEnd();
            }

            // trace the expected path and code
            Trace.Write("EXPECTED CODE: ");
            Trace.WriteLine(expectedPath);
            Trace.WriteLine(expectedCode);
            Trace.WriteLine(string.Empty);

            // read the code actually output by the process
            string outputCode;
            using (var reader = new StreamReader(outputPath))
            {
                outputCode = reader.ReadToEnd();
            }

            // trace the output path and code
            Trace.Write("OUTPUT CODE: ");
            Trace.WriteLine(outputPath);
            Trace.WriteLine(outputCode);
            Trace.WriteLine(string.Empty);

            // compare them
            var exactMatch = string.CompareOrdinal(expectedCode, outputCode) == 0;
            Assert.That(exactMatch, "Expected and actual Output code does not match!");
        }
    }
}
