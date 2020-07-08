using GeradorCodigoGauge.Console;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GaugeTestsGenerator.Azure.IntegrationTests
{
    public class GeneratorTests
    {
        /// <summary>
        /// An integration test that assert entire result before and after refactoring
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task Execute_InputDataFromBeforeRefactoring_ResultFilesIsEqualToBefore()
        {
            // Arrange
            var generator = new Generator();

            // copying files to avoid too long file name exception 
            string resultPath = @"c:\\genGaugeTest\";
            DeleteDirectory(resultPath);
            // using robocopy because xcopy fail for some files
            string copyCommand = $"robocopy {Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\oldData\")} {resultPath} /s";
            ExecuteCommand(copyCommand);

            // Injecting data to avoid Azure api
            string jsonLstTestSuiteFolhas = File.ReadAllText($"{resultPath}_lstTestSuiteFolhas.json", Encoding.Default);
            string jsonLstTestSuite2Nivel = File.ReadAllText($"{resultPath}_lstTestSuite2Nivel.json", Encoding.Default);
            Generator._lstTestSuiteFolhas = JsonConvert.DeserializeObject<List<TestSuite>>(jsonLstTestSuiteFolhas);
            Generator._lstTestSuite2Nivel = JsonConvert.DeserializeObject<List<TestSuite>>(jsonLstTestSuite2Nivel);
            Generator._filePath = $"{resultPath}importacao.csv";
            Generator.outputPath = $"{resultPath}output";

            // Act
            await generator.Execute(new string[0]);

            // Assert
            FileCompareHelper.AssertDirectoriesIdentical(resultPath + "old_output", resultPath + "output");
        }

        private static void DeleteDirectory(string resultPath)
        {
            try
            {
                Directory.Delete(resultPath, true);
            }
            catch (Exception)
            {
            }
        }

        private void ExecuteCommand(string command)
        {
            var processInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardInput = true;

            var process = Process.Start(processInfo);

            process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                Console.WriteLine("output>>" + e.Data);
            process.BeginOutputReadLine();

            process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                Console.WriteLine("error>>" + e.Data);
            process.BeginErrorReadLine();

            process.WaitForExit();

            Console.WriteLine("ExitCode: {0}", process.ExitCode);
            process.Close();
        }
    }
}
