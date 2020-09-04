using CsvHelper;
using Dasync.Collections;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace GeradorCodigoGauge.Console
{
    public class Generator
    {
        public static string outputPath = "output";
        const string outputPathSpec = "specs";
        const string outputPathImp = "specs";
        const string outputPathPage = "specs";
        private static string outputPathCsv = "specs";
        const string solutionNamespace = "SolutionTest.Selenium";

        private static string azureBaseUrl = "https://gauge-tests-generator.visualstudio.com/gauge-tests-generator/";
        private static string azureApiVersion = "api-version=5.0";
        private static string azureToken = "";

        private static string testPlanId = "13694";
        private static string csvSeparador = ",";
        // Escolher a suite que deseja gerar, ou gerar todas as suites do testplan
        private static int[] _idsTestSuite = null; // Usar linha de comando para gerar por id //new[] { 17570 }; 
        public static string _filePath = "importacao.csv";

        private static Regex _regexPalavraAspas = new Regex("\"(([^\"\\\\]|\\\\.)*)(\"|\\\\\")", RegexOptions.Compiled);
        private static Regex _regexPalavraTag = new Regex("<(([^<])*)>", RegexOptions.Compiled);
        private static Regex _regexParametroStepTestCase = new Regex(@"\s@([^\s.]*)", RegexOptions.Compiled);
        private static Regex _regexEspacosExcedentes = new Regex(@"\s\s", RegexOptions.Compiled);
        private static Regex _regexDiferenteDeLetraNumero = new Regex(@"[^0-9a-zA-Z]+", RegexOptions.Compiled);
        private static Regex _regexAgrupamentoPreenchimento = new Regex(@"(?i)(preencher|marcar|selecionar|alterar)\s.*\s@([^\s])+", RegexOptions.Compiled);
        private static Regex _regexAgrupamentoAssert = new Regex(@"(?i)(deve|conter)\s.*\s@([^\s])+", RegexOptions.Compiled);
        private static Regex _regexSessaoAgrupamento = new Regex(@"(?i)(?:se[çc][aã]o|pop-up)\s(.*?)\s(?:o|no|na|marcar|selecionar|preencher)\s", RegexOptions.Compiled);
        const string replaceRegexPalavraAspasPorTag = @"<$1>";
        const string replaceParametroStepTestCase = "\"$1\"\\s";
        private static Dictionary<string, string> _dePara = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("dePara.json", Encoding.Default));
        private static List<SharedStepConfig> _lstSharedStep = JsonConvert.DeserializeObject<List<SharedStepConfig>>(File.ReadAllText("sharedStepsDePara.json", Encoding.Default));
        private static Dictionary<string, WorkItem> _dicSharedParameters;
        public static List<TestSuite> _lstTestSuite2Nivel;
        public static List<TestSuite> _lstTestSuiteFolhas;
        private static List<SharedStepDefinition> _lstSharedStepDef;
        private static List<SpecDefinition> _suiteSpecs;
        private static List<StepGroupConfig> _lstStepGroupConfig = new List<StepGroupConfig>();
        private static bool _usarTabelaSharedStep = true;
        private static bool _agruparPreenchimento = true;
        private static bool _agruparAssert = true;

        const string tab = "        ";

        public async Task Execute(string[] args)
        {
            ConfigurarGerador(args);

            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, true);

            // Carregar Suite de testes
            await CarregarTestSuite();

            // Ler .csv
            List<WorkItem> lstWorkItem = LerArquivo(_filePath);

            ProcessarSharedSteps(lstWorkItem);

            ProcessarTestSuites(lstWorkItem);
        }

        private static void ConfigurarGerador(string[] args)
        {
            if (args.Length > 0)
            {
                _idsTestSuite = args[0].Split(',').Select(x => int.Parse(x)).ToArray();
            }

            if (_agruparPreenchimento)
                _lstStepGroupConfig.Add(
                    new StepGroupConfig()
                    {
                        RegexAgrupamento = _regexAgrupamentoPreenchimento,
                        MetodoProcessarAgrupamento = ProcessarAgrupamentoPreenchimento
                    });

            if (_agruparAssert)
                _lstStepGroupConfig.Add(
                    new StepGroupConfig()
                    {
                        RegexAgrupamento = _regexAgrupamentoAssert,
                        MetodoProcessarAgrupamento = ProcessarAgrupamentoAssert
                    });

            if (!_usarTabelaSharedStep)
                outputPathCsv = "cvs_nao_copiar";
        }

        private static void ProcessarTestSuites(List<WorkItem> lstWorkItem)
        {
            // Percorrendo testcases
            foreach (var testSuite in _lstTestSuiteFolhas)
            {
                var suiteParent = _lstTestSuite2Nivel.FirstOrDefault(x => x.id.ToString() == testSuite.parent.id);
                // caminho seguindo estrutura das test suites. 2 níveis inferiores de suite apenas.
                string pathSuite = DePara(suiteParent.name);
                string nomeSuite = DePara(testSuite.name);

                var result = ProcessarSuite(lstWorkItem, testSuite);

                string nomeSuiteTratado = TratarNomeArquivo(nomeSuite, false);
                string pathSuiteTratado = TratarNomeArquivo(pathSuite, false);

                SalvarArquivos(
                    result.CodigoSpec,
                    result.CodigoImp,
                    Path.Combine(outputPath, outputPathSpec, pathSuiteTratado, nomeSuiteTratado, TratarNomeArquivo(nomeSuite + ".spec")),
                    Path.Combine(outputPath, outputPathImp, pathSuiteTratado, nomeSuiteTratado, TratarNomeArquivo(nomeSuite + " Page Step.cs")),
                    Path.Combine(outputPath, outputPathPage, pathSuiteTratado, nomeSuiteTratado, TratarNomeArquivo(nomeSuite + " Page.cs")),
                    () => ArquivoInicialSpec(nomeSuite, testSuite.id.ToString()),
                    () => ArquivoInicialImp(pathSuiteTratado, nomeSuiteTratado),
                    () => ArquivoInicialPage(pathSuiteTratado, nomeSuiteTratado));

                SalvarCsv(result.Csv, Path.Combine(outputPath, outputPathCsv, pathSuiteTratado, nomeSuiteTratado));
            }
        }

        private static void SalvarCsv(List<(string csv, string nomeArquivo)> lstCsv, string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            foreach (var csv in lstCsv)
            {
                string pathCsv = Path.Combine(path, csv.nomeArquivo);
                File.WriteAllText(pathCsv, csv.csv);
            }
        }

        private static List<WorkItem> ProcessarSharedParameters(List<WorkItem> lstWorkItem)
        {
            // Separando Shared Parameters
            var lstSharedParameter = lstWorkItem
                .Where(x => x.Type == "Shared Parameter")
                .ToList();

            _dicSharedParameters = lstSharedParameter
                .ToDictionary(x => x.Id);

            var tempLstWorkItem = lstWorkItem.Except(lstSharedParameter).ToList();
            lstWorkItem.Clear();
            lstWorkItem.AddRange(tempLstWorkItem);

            lstSharedParameter.ForEach(x => TratarNomeParametros(x));

            return lstSharedParameter;
        }

        private static string TratarNomeArquivo(string nomeArquivo, bool comExtensao = true)
        {
            string extensao = string.Empty;
            int indiceExtensao = nomeArquivo.LastIndexOf('.');

            if (comExtensao && indiceExtensao != -1)
            {
                extensao = nomeArquivo.Substring(indiceExtensao, nomeArquivo.Length - indiceExtensao);
                nomeArquivo = nomeArquivo.Remove(indiceExtensao, nomeArquivo.Length - indiceExtensao);
            }

            nomeArquivo = removerAcentos(nomeArquivo);

            TextInfo textInfo = new CultureInfo("pr-Br", false).TextInfo;
            nomeArquivo = textInfo.ToTitleCase(nomeArquivo.ToLower());

            nomeArquivo = _regexDiferenteDeLetraNumero.Replace(nomeArquivo, "");

            // Cortando nomes maiores que 80 caracteres
            if (nomeArquivo.Length > 80)
            {
                int indice;
                nomeArquivo = nomeArquivo.Substring(0, 65) +
                    ((indice = nomeArquivo.LastIndexOf("Tabela")) != -1 ? nomeArquivo.Substring(indice, nomeArquivo.Length - indice) :
                    (indice = nomeArquivo.LastIndexOfAny("0123456789".ToCharArray())) != -1 ? nomeArquivo.Substring(indice, nomeArquivo.Length - indice) :
                    string.Empty);
            }

            if (comExtensao && indiceExtensao != -1)
                nomeArquivo += extensao;

            return nomeArquivo;
        }

        private static string removerAcentos(string texto)
        {
            string comAcentos = "ÄÅÁÂÀÃäáâàãÉÊËÈéêëèÍÎÏÌíîïìÖÓÔÒÕöóôòõÜÚÛüúûùÇç";
            string semAcentos = "AAAAAAaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUuuuuCc";

            for (int i = 0; i < comAcentos.Length; i++)
            {
                texto = texto.Replace(comAcentos[i].ToString(), semAcentos[i].ToString());
            }
            return texto;
        }

        private static void SalvarArquivos(
            string codigoSpec,
            string codigoImp,
            string pathArquivoSpec,
            string pathArquivoImp,
            string pathArquivoPage,
            Func<string> arquivoSpecInicialFactory,
            Func<string> arquivoImpInicialFactory,
            Func<string> arquivoPageInicialFactory)
        {
            // Criar arquivo spec, se precisar
            if (!File.Exists(pathArquivoSpec))
            {
                string arquivoSpecInicial = arquivoSpecInicialFactory.Invoke();
                Directory.CreateDirectory(Path.GetDirectoryName(pathArquivoSpec));
                File.AppendAllText(pathArquivoSpec, arquivoSpecInicial);
            }

            File.AppendAllText(pathArquivoSpec, codigoSpec);

            // Criar arquivo implementação steps, se precisar            
            if (!File.Exists(pathArquivoImp))
            {
                string arquivoImpInicial = arquivoImpInicialFactory.Invoke();
                Directory.CreateDirectory(Path.GetDirectoryName(pathArquivoImp));
                File.AppendAllText(pathArquivoImp, arquivoImpInicial);
            }

            var arquivoImp = File.ReadAllLines(pathArquivoImp).ToList();
            int contador = 0;
            int indiceFinalClasse = 0;
            for (int i = arquivoImp.Count - 1; i > 0; i--)
            {
                if (arquivoImp[i].Contains("}"))
                    contador += 1;

                if (contador == 2)
                {
                    indiceFinalClasse = i;
                    break;
                }
            }

            arquivoImp.Insert(indiceFinalClasse, codigoImp);
            File.WriteAllText(pathArquivoImp, string.Join(Environment.NewLine, arquivoImp));

            // Criar arquivo page, se precisar            
            if (!File.Exists(pathArquivoPage))
            {
                string arquivoPageInicial = arquivoPageInicialFactory.Invoke();
                Directory.CreateDirectory(Path.GetDirectoryName(pathArquivoPage));
                File.AppendAllText(pathArquivoPage, arquivoPageInicial);
            }
        }

        private static ResultadoProcessamentoString ProcessarSuite(List<WorkItem> lstWorkItem, TestSuite testSuite)
        {
            var idsOrdenados = testSuite.IdsTestCasesOrdenados.ToList();

            var lstTestCase = lstWorkItem
                .Where(x => x.Type == "Test Case" && testSuite.IdsTestCases.Contains(x.Id))
                .OrderBy(x => idsOrdenados.IndexOf(x.Id))
                .ToList();

            lstWorkItem = lstWorkItem.Except(lstTestCase).ToList();

            List<ScenarioDefinition> lstScenario = new List<ScenarioDefinition>();
            _suiteSpecs = new List<SpecDefinition>();

            // Para cada caso de teste
            foreach (var testCase in lstTestCase)
            {
                ScenarioDefinition scenario = new ScenarioDefinition();
                scenario.StepsSpec = new List<SpecDefinition>();
                scenario.StepsImp = new List<ImpDefinition>();

                TratarNomeParametros(testCase);

                scenario.Title = ($"## {DePara(testCase.Title)} [{testCase.Id}]");

                ProcessarSteps(testCase, scenario.StepsSpec, scenario.StepsImp, testSuite);

                scenario.Csv = ExtrairCsvCenario(testCase);

                lstScenario.Add(scenario);
            }

            var lstImps = lstScenario
                .SelectMany(x => x.StepsImp)
                .ToList();

            var lstImpRepetidos = lstImps
                .Except(lstImps
                    .DistinctBy(x => x.Code).ToList())
                .Select(x => x.Code)
                .Distinct()
                .ToList();

            return TransformarEmTexto(lstScenario, lstImpRepetidos);
        }

        private static string ExtrairCsvCenario(WorkItem workItem)
        {
            if (workItem.ParametersValue == null &&
                workItem.SharedParametersMap == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();

            if (workItem.ParametersValue != null)
            {
                if (workItem.ParametersValue.Columns.Count <= 0)
                    return string.Empty;

                IEnumerable<string> columns = workItem.ParametersValue.Columns
                    .OfType<DataColumn>()
                    .Select(x =>
                            string.IsNullOrWhiteSpace(x.ColumnName) ? string.Empty : string.Concat("\"", x.ColumnName.Replace("\"", "\"\""), "\""));
                sb.AppendLine(string.Join(csvSeparador, columns));

                foreach (System.Data.DataRow row in workItem.ParametersValue.Rows)
                {
                    IEnumerable<string> fields = row.ItemArray
                        .Select(field =>
                            string.IsNullOrWhiteSpace(field?.ToString()) ? string.Empty : string.Concat("\"", field.ToString().Replace("\"", "\"\""), "\""));
                    sb.AppendLine(string.Join(csvSeparador, fields));
                }
            }
            else
            {
                var paramMap = workItem.SharedParametersMap.parameterMap
                    .FirstOrDefault();

                if (paramMap == null)
                    return string.Empty;

                WorkItem sharedParameter;

                if (!_dicSharedParameters.TryGetValue(paramMap.sharedParameterDataSetId.ToString(), out sharedParameter))
                    return string.Empty;

                if (sharedParameter.ParameterSet.ParamData.DataRow.Count <= 0)
                    return string.Empty;

                string cabecalho = string.Join(csvSeparador, sharedParameter.ParameterSet.ParamNames.Param
                   .Select(x =>
                       string.IsNullOrWhiteSpace(x) ? string.Empty : string.Concat("\"", x.Trim().Replace("\"", "\"\""), "\""))
                   .ToArray());

                sb.AppendLine(cabecalho);

                foreach (var row in sharedParameter.ParameterSet.ParamData.DataRow)
                {
                    var valores = sharedParameter.ParameterSet.ParamNames.Param
                        .Select(x =>
                        {
                            var kvp = row.Kvp.FirstOrDefault(y => y.Key == x);

                            return string.IsNullOrWhiteSpace(kvp?.Value) ? string.Empty : string.Concat("\"", kvp.Value.Replace("\"", "\"\""), "\"");
                        })
                        .ToArray();

                    sb.AppendLine(string.Join(csvSeparador, valores));
                }
            }

            return sb.ToString();
        }

        private static void ProcessarSharedSteps(List<WorkItem> lstWorkItem)
        {
            ProcessarSharedParameters(lstWorkItem);

            List<WorkItem> lstSharedSteps = SepararSharedStep(lstWorkItem);

            List<SharedStepDefinition> lstSharedStepDef = new List<SharedStepDefinition>();

            // Para cada shared step
            foreach (var sharedStep in lstSharedSteps)
            {
                SharedStepDefinition sharedStepDef = new SharedStepDefinition();
                sharedStepDef.StepsSpec = new List<SpecDefinition>();
                sharedStepDef.StepsImp = new List<ImpDefinition>();

                TratarNomeParametros(sharedStep);

                _suiteSpecs = new List<SpecDefinition>();

                ProcessarSteps(sharedStep, sharedStepDef.StepsSpec, sharedStepDef.StepsImp, null, true);

                sharedStepDef.StepsImp = sharedStepDef.StepsImp.DistinctBy(x => x.Code).ToList();

                TratarCodigoTituloSharedStep(sharedStep, sharedStepDef);

                if (sharedStepDef.TitleSpec.IsTableParameter)
                {
                    TratarImplementacaoTituloSharedStepComParametroTabela(sharedStepDef);
                }

                sharedStepDef.Id = sharedStep.Id;

                lstSharedStepDef.Add(sharedStepDef);
            }

            foreach (var sharedStep in lstSharedStepDef)
            {
                var (codigoSpec, codigoImp) = TransformarEmTexto(sharedStep);

                // Removendo parâmetros do nome do arquivo
                string tituloTratado = _regexPalavraTag.Replace(sharedStep.Title, "");
                tituloTratado = TratarNomeArquivo(tituloTratado, false);

                SalvarArquivos(
                    codigoSpec,
                    codigoImp,
                    Path.Combine(outputPath, outputPathSpec, "Shared", tituloTratado, tituloTratado + ".cpt" + (sharedStep.TitleTableParameterAcessingFields ? ".txt" : "")),
                    Path.Combine(outputPath, outputPathImp, "Shared", tituloTratado, tituloTratado + "PageStep.cs"),
                    Path.Combine(outputPath, outputPathPage, "Shared", tituloTratado, tituloTratado + "Page.cs"),
                    () => ArquivoInicialSpec(sharedStep.Title),
                    () => ArquivoInicialImp("Shared", tituloTratado),
                    () => ArquivoInicialPage("Shared", tituloTratado));
            }

            _lstSharedStepDef = lstSharedStepDef;
        }

        private static List<WorkItem> SepararSharedStep(List<WorkItem> lstWorkItem)
        {
            var hsIdsUsadosSharedSteps = new HashSet<string>(lstWorkItem
                .Where(x =>
                    x.Type == "Test Case" &&
                    _lstTestSuiteFolhas.Any(y => y.IdsTestCases.Contains(x.Id)))
                .Select(x =>
                {
                    return ObterIdsSharedStepsUsadosWorkItem(x);
                })
                .SelectMany(x => x)
                .Distinct()
                .ToList());

            HashSet<string> hsIdsSSRecursivos = hsIdsUsadosSharedSteps;
            bool possuiIdsRecursivos = true;
            while (possuiIdsRecursivos)
            {
                hsIdsUsadosSharedSteps.AddRange(hsIdsSSRecursivos);

                hsIdsSSRecursivos = new HashSet<string>(lstWorkItem
                    .Where(x =>
                        hsIdsSSRecursivos.Contains(x.Id))
                    .Select(x =>
                    {
                        return ObterIdsSharedStepsUsadosWorkItem(x);
                    })
                    .SelectMany(x => x)
                    .Distinct()
                    .ToList());

                if (hsIdsSSRecursivos.Count == 0)
                    possuiIdsRecursivos = false;
            }

            var lstSharedSteps = lstWorkItem
                .Where(x =>
                    x.Type == "Shared Steps" &&
                    hsIdsUsadosSharedSteps.Contains(x.Id))
                .ToList();

            var tempLstWorkItem = lstWorkItem.Except(lstSharedSteps).ToList();
            lstWorkItem.Clear();
            lstWorkItem.AddRange(tempLstWorkItem);

            return lstSharedSteps;
        }

        private static string[] ObterIdsSharedStepsUsadosWorkItem(WorkItem x)
        {
            if (x.Steps != null)
            {
                List<string> ids = new List<string>();
                bool hasSteps = true;
                Compref actualSharedStep = x.Steps.Compref;
                while (hasSteps)
                {
                    if (actualSharedStep != null)
                        ids.Add(actualSharedStep.Ref);

                    if (actualSharedStep?.compref != null)
                    {
                        actualSharedStep = actualSharedStep.compref;
                    }
                    else
                        hasSteps = false;
                }
                return ids.ToArray();
            }
            else
                return new string[0];
        }

        private static void TratarImplementacaoTituloSharedStepComParametroTabela(SharedStepDefinition sharedStepDef)
        {
            sharedStepDef.TitleTableParameterAcessingFields = true;

            var titleImp = GerarCodigoImplementacao(sharedStepDef.TitleSpec);

            string imp = $"{Environment.NewLine}    {tab}var row = tabela.GetTableRows()[0];{Environment.NewLine}";
            string chamadaAosPassos = string.Join(Environment.NewLine, sharedStepDef.StepsImp
                .Select(x =>
                {
                    string parametros = string.Join(", ", x.Parameters.Select(y => y.tipo == "Table" ? "tabela" : $"row.GetCell(\"{y.parametro}\")").ToArray());
                    return $"   {tab}{x.MethodName}({parametros});";
                })
                .ToArray());

            imp += chamadaAosPassos;
            imp += Environment.NewLine;

            int indiceComecoMetodo = titleImp.Code.IndexOf("{") + 1;
            titleImp.Code = titleImp.Code.Insert(indiceComecoMetodo, imp);

            sharedStepDef.TitleImp = titleImp;

            sharedStepDef.StepsImp.Insert(0, titleImp);
        }

        private static void ProcessarSteps(
            WorkItem workItem,
            List<SpecDefinition> specDefinitions,
            List<ImpDefinition> impDefinitions,
            TestSuite testSuite,
            bool isSharedStep = false)
        {
            if (workItem.Steps != null)
            {
                bool hasSteps = true;

                ProcessarListaSteps(workItem, workItem.Steps.Step, specDefinitions, impDefinitions, testSuite, isSharedStep);

                Compref actualSharedStep = workItem.Steps.Compref;
                List<Step> actualSteps = workItem.Steps.Compref?.Step;

                while (hasSteps)
                {
                    // processar os shared steps criados à partir de um workitem do azure devops
                    AdicionarSharedStepCriadoWorkItem(workItem, specDefinitions, actualSharedStep);

                    ProcessarListaSteps(workItem, actualSteps, specDefinitions, impDefinitions, testSuite, isSharedStep);

                    if (actualSharedStep?.compref != null)
                    {
                        actualSharedStep = actualSharedStep.compref;
                        actualSteps = actualSharedStep.Step ?? new List<Step>();
                    }
                    else
                        hasSteps = false;
                }
            }
        }

        private static void AdicionarSharedStepCriadoWorkItem(WorkItem workItem, List<SpecDefinition> specDefinitions, Compref actualSharedStep)
        {
            if (actualSharedStep != null)
            {
                var spDef = BuscarSharedStepPorId(actualSharedStep.Ref);

                if (spDef != null)
                {
                    string comment = null;
                    if (!spDef.TitleSpec.IsTableParameter && spDef.TitleSpec.UsedParameters.Length > 0)
                        comment = "Parâmetros: " + string.Join(", ", spDef.TitleSpec.UsedParameters.ToArray());

                    var specDef = new SpecDefinition()
                    {
                        Code = spDef.TitleSpecStepCode,
                        OriginalText = spDef.TitleSpec.OriginalText,
                        OriginalCode = spDef.TitleSpec.OriginalCode,
                        Comment = comment != null ? new List<string>() { comment } : new List<string>(),
                        IsShared = true,
                        UsedParameters = spDef.TitleSpec.UsedParameters,
                        IsTableParameter = spDef.TitleSpec.IsTableParameter
                    };

                    PreencherValorParametros(workItem, spDef.TitleSpec.UsedParameters, specDef);

                    specDefinitions.Add(specDef);
                }
            }
        }

        private static void ProcessarListaSteps(
            WorkItem testCase,
            List<Step> steps,
            List<SpecDefinition> specDefinitions,
            List<ImpDefinition> impDefinitions,
            TestSuite testSuite,
            bool isSharedStep = false)
        {
            // Para cada step dentro do caso de teste
            for (int i = 0; i < steps?.Count; i++)
            {
                var step = steps[i];

                var stSpec = BuscarSharedStepConfig(testCase, step.ParameterizedString[0].Text);

                if (stSpec != null)
                {
                    specDefinitions.Add(stSpec);
                }
                else if (AgruparSteps(_lstStepGroupConfig, testCase, steps, specDefinitions, impDefinitions, ref i, testSuite, isSharedStep))
                    continue;
                else
                {
                    var specDef = GerarCodigoSpecStep(testCase, step, GerarNomeTela(testSuite, testCase), isSharedStep);

                    // Passo sem descrição
                    if (specDef.Code == "* ")
                        continue;

                    specDefinitions.Add(specDef);

                    var impDef = GerarCodigoImplementacao(specDef);
                    impDefinitions.Add(impDef);
                }
            }
        }

        private static bool AgruparSteps(
            List<StepGroupConfig> lstGroupConfig,
            WorkItem testCase,
            List<Step> steps,
            List<SpecDefinition> specDefinitions,
            List<ImpDefinition> impDefinitions,
            ref int index,
            TestSuite suite,
            bool isSharedStep = false)
        {
            bool agrupou = false;

            foreach (var groupConfig in lstGroupConfig)
            {
                if (agrupou = AgruparSteps(groupConfig, testCase, steps, specDefinitions, impDefinitions, ref index, suite, isSharedStep))
                    break;
            }
            return agrupou;
        }

        private static bool AgruparSteps(
            StepGroupConfig groupConfig,
            WorkItem testCase,
            List<Step> steps,
            List<SpecDefinition> specDefinitions,
            List<ImpDefinition> impDefinitions,
            ref int index,
            TestSuite suite,
            bool isSharedStep = false)
        {
            List<Step> stepsAgrupados = new List<Step>();
            string secao = string.Empty;
            bool agrupou = true;

            while (index < steps.Count)
            {
                string textoStepAtual = TratarTextoStepParaAgrupamento(steps[index].ParameterizedString[0].Text);

                if (stepsAgrupados.Count == 0)
                {
                    string textoStepSeguinte = steps.Count >= (index + 2) ? TratarTextoStepParaAgrupamento(steps[index + 1].ParameterizedString[0].Text) : "";

                    // Se não é o último passo e o atual e o próximo precisam ser agrupados
                    if (steps.Count >= (index + 2) &&
                        groupConfig.RegexAgrupamento.IsMatch(textoStepAtual) &&
                        groupConfig.RegexAgrupamento.IsMatch(textoStepSeguinte) &&
                        ((secao = ExtrairSecao(textoStepAtual)) == ExtrairSecao(textoStepSeguinte)))
                    {
                        stepsAgrupados.Add(steps[index]);
                    }
                    else
                    {
                        agrupou = false;
                        break;
                    }
                }
                else
                {
                    if (groupConfig.RegexAgrupamento.IsMatch(textoStepAtual) &&
                        secao == ExtrairSecao(textoStepAtual))
                    {
                        // último step. Processar agrupar
                        if (steps.Count == (index + 1))
                        {
                            stepsAgrupados.Add(steps[index]);
                            groupConfig.MetodoProcessarAgrupamento(testCase, specDefinitions, impDefinitions, stepsAgrupados, suite, secao, isSharedStep);
                            secao = string.Empty;
                            stepsAgrupados.Clear();
                            break;
                        }
                        else
                            stepsAgrupados.Add(steps[index]);
                    }
                    else
                    {
                        groupConfig.MetodoProcessarAgrupamento(testCase, specDefinitions, impDefinitions, stepsAgrupados, suite, secao, isSharedStep);
                        secao = string.Empty;
                        stepsAgrupados.Clear();
                        index--;
                        break;
                    }
                }

                index++;
            }

            return agrupou;
        }

        private static string TratarTextoStepParaAgrupamento(string texto)
        {
            texto = _regexPalavraTag.Replace(texto, "");
            return texto.Replace("&nbsp;", " ").Replace("\r\n", " ").Replace("\n", " ");
        }

        private static string ExtrairSecao(string texto)
        {
            // desabilitando o agrupamento por sessão
            return string.Empty;
            //var match = _regexSessaoAgrupamento.Match(texto);
            //return match?.Groups.Count > 1 ? match.Groups[1].Value : string.Empty;
        }

        private static void ProcessarAgrupamentoAssert(
           WorkItem testCase,
           List<SpecDefinition> specDefinitions,
           List<ImpDefinition> impDefinitions,
           List<Step> stepsAgrupados,
           TestSuite suite,
           string secao,
           bool isSharedStep = false)
        {
            string nomeTela = GerarNomeTela(suite, testCase);
            string passo;
            if (string.IsNullOrWhiteSpace(secao))
                passo = $"Verificar os campos da tela";
            else
                passo = $"Na seção {secao}, verificar os campos da tela";

            SpecDefinition specGrupoDef = GerarCodigoSpecStep(testCase, passo, nomeTela, isSharedStep, true);

            ImpDefinition impDef = GerarCodigoImplementacaoPassoAgrupadoAssert(testCase, stepsAgrupados, isSharedStep, specGrupoDef);

            PreencherValorParametros(testCase, specGrupoDef.UsedParameters, specGrupoDef, isSharedStep);

            specDefinitions.Add(specGrupoDef);
            impDefinitions.Add(impDef);

            stepsAgrupados.Clear();
        }

        private static string GerarNomeTela(TestSuite suite, WorkItem testCase)
        {
            string nomeTela = !string.IsNullOrWhiteSpace(suite?.name) ? DePara(suite.name) : DePara(testCase.Title);
            nomeTela = TratarNomeArquivo(nomeTela) + "PageStep";
            return nomeTela;
        }

        private static void ProcessarAgrupamentoPreenchimento(
            WorkItem testCase,
            List<SpecDefinition> specDefinitions,
            List<ImpDefinition> impDefinitions,
            List<Step> stepsAgrupados,
            TestSuite suite,
            string secao,
            bool isSharedStep = false)
        {
            string nomeTela = GerarNomeTela(suite, testCase);
            string passo;
            if (string.IsNullOrWhiteSpace(secao))
                passo = $"Preencher os campos da tela";
            else
                passo = $"Na seção {secao}, preencher os campos da tela";

            SpecDefinition specGrupoDef = GerarCodigoSpecStep(testCase, passo, nomeTela, isSharedStep, true);

            ImpDefinition impDef = GerarCodigoImplementacaoPassoAgrupadoPreenchimento(testCase, stepsAgrupados, isSharedStep, specGrupoDef);

            PreencherValorParametros(testCase, specGrupoDef.UsedParameters, specGrupoDef, isSharedStep);

            specDefinitions.Add(specGrupoDef);
            impDefinitions.Add(impDef);

            stepsAgrupados.Clear();
        }

        private static ImpDefinition GerarCodigoImplementacaoPassoAgrupadoAssert(
            WorkItem testCase,
            List<Step> stepsAgrupados,
            bool isSharedStep,
            SpecDefinition specGrupoDef)
        {
            var impDef = GerarCodigoImplementacao(specGrupoDef);
            specGrupoDef.Comment = new List<string>();

            string imp = $@"
    {tab}var row = tabela.GetTableRows()[0];
    {tab}using (new AssertionScope())
    {tab}{{
";
            string chamadaAosPassos = string.Join(Environment.NewLine, stepsAgrupados
                .Select(x =>
                {
                    var specPasso = GerarCodigoSpecStep(testCase, x, null, isSharedStep);
                    specGrupoDef.Comment.Add(specPasso.Code.Substring(2, specPasso.Code.Length - 2));

                    return $"   {tab}    " + GerarImplementacaoAssertCampo(specPasso, true);
                })
                .ToArray());

            imp += chamadaAosPassos + $@"{Environment.NewLine}    {tab}}}";

            int indiceComecoMetodo = impDef.Code.IndexOf("{") + 1;
            impDef.Code = impDef.Code.Insert(indiceComecoMetodo, imp);
            return impDef;
        }

        private static string GerarImplementacaoAssertCampo(SpecDefinition specDefinition, bool agrupado)
        {
            string parametro;
            string valorParametro;

            parametro = specDefinition.UsedParameters[0];
            valorParametro = agrupado ?
                parametro == "tabela" ? "tabela" : $"row.GetCell(\"{parametro}\")" :
                parametro;

            string specPassoCodeLower = specDefinition.Code.ToLower();

            string obterCampo = "Retornar" + parametro.First().ToString().ToUpper() + parametro.Substring(1, parametro.Length - 1);

            string tipoCampo =
                specPassoCodeLower.Contains("campo") ? "campo" :
                specPassoCodeLower.Contains("checkbox") ? "checkbox" :
                specPassoCodeLower.Contains("combobox") ? "combobox" : string.Empty;

            if (tipoCampo == "checkbox")
                valorParametro = "Convert.ToBoolean(" + valorParametro + ")";

            return $"_page.{obterCampo}().Should().Be({valorParametro});";
        }

        private static ImpDefinition GerarCodigoImplementacaoPassoAgrupadoPreenchimento(
            WorkItem testCase,
            List<Step> stepsAgrupados,
            bool isSharedStep,
            SpecDefinition specGrupoDef)
        {
            var impDef = GerarCodigoImplementacao(specGrupoDef);
            specGrupoDef.Comment = new List<string>();

            string imp = $"{Environment.NewLine}    {tab}var row = tabela.GetTableRows()[0];{Environment.NewLine}";
            string chamadaAosPassos = string.Join(Environment.NewLine, stepsAgrupados
                .Select(x =>
                {
                    var specPasso = GerarCodigoSpecStep(testCase, x, null, isSharedStep);
                    specGrupoDef.Comment.Add(specPasso.Code.Substring(2, specPasso.Code.Length - 2));

                    return $"   {tab}" + GerarImplementacaoPreenchimentoCampo(specPasso, true);
                })
                .ToArray());

            imp += chamadaAosPassos;

            int indiceComecoMetodo = impDef.Code.IndexOf("{") + 1;
            impDef.Code = impDef.Code.Insert(indiceComecoMetodo, imp);
            return impDef;
        }

        private static string GerarImplementacaoPreenchimentoCampo(SpecDefinition specDefinition, bool agrupado)
        {
            // Quando for o "alterar campo", pode vir com 2 parâmetros. Usando apenas o 2º
            string parametro = agrupado ?
                specDefinition.UsedParameters.Length > 0 ? specDefinition.UsedParameters.Last() : specDefinition.UsedParameters[0]
                : specDefinition.UsedParameters[0];
            string valorParametro = agrupado ?
                parametro == "tabela" ? "tabela" : $"row.GetCell(\"{parametro}\")" :
                parametro;

            string specPassoCodeLower = specDefinition.Code.ToLower();
            string campoPagina =
                specPassoCodeLower.Contains("preencher") ? "Preencher" :
                specPassoCodeLower.Contains("marcar") ? "Marcar" :
                specPassoCodeLower.Contains("selecionar") ? "Selecionar" : string.Empty;
            campoPagina += parametro.First().ToString().ToUpper() + parametro.Substring(1, parametro.Length - 1);

            if (campoPagina.StartsWith("Marcar"))
                valorParametro = "Convert.ToBoolean(" + valorParametro + ")";

            return $"_page.{campoPagina}({valorParametro});";
        }

        private static (string codigoSpec, string codigoImp) TransformarEmTexto(SharedStepDefinition sharedStep)
        {
            StringBuilder codigoSpec = new StringBuilder();
            StringBuilder codigoImp = new StringBuilder();

            sharedStep.StepsSpec.Select(y =>
            {
                codigoSpec.AppendLine(y.Code);
                if (y.Comment != null)
                    y.Comment.Select(z => codigoSpec.AppendLine(z)).ToArray();
                return 1;
            }).ToArray();
            codigoSpec.AppendLine();

            sharedStep.StepsImp.Select(y =>
            {
                codigoImp.AppendLine(y.Code);
                codigoImp.AppendLine();
                return codigoImp;
            }).ToArray();

            return (codigoSpec.ToString(), codigoImp.ToString());
        }

        private static ResultadoProcessamentoString TransformarEmTexto(List<ScenarioDefinition> lst, List<string> impRepetidas)
        {
            StringBuilder codigoSpec = new StringBuilder();
            StringBuilder codigoImp = new StringBuilder();
            var lstCsv = new List<(string csv, string nomeArquivo)>();

            var dicImpRepetidasProcessadas = impRepetidas.ToDictionary(x => x, x => false);

            lst.ForEach(x =>
            {
                codigoSpec.AppendLine(x.Title);
                x.StepsSpec.Select(y =>
                {
                    codigoSpec.AppendLine(y.Code);
                    if (y.Comment != null)
                        y.Comment.Select(z => codigoSpec.AppendLine(z)).ToArray();
                    return 1;
                }).ToArray();
                codigoSpec.AppendLine();

                x.StepsImp.Select(y =>
                {
                    if (dicImpRepetidasProcessadas.GetValueOrDefault(y.Code))
                        return codigoImp;

                    codigoImp.AppendLine(y.Code);
                    codigoImp.AppendLine();

                    if (dicImpRepetidasProcessadas.ContainsKey(y.Code))
                        dicImpRepetidasProcessadas[y.Code] = true;

                    return codigoImp;
                }).ToArray();

                if (!string.IsNullOrWhiteSpace(x.Csv))
                    lstCsv.Add((x.Csv, GerarNomeArquivoCsv(x.Title)));
            });

            return new ResultadoProcessamentoString()
            {
                CodigoSpec = codigoSpec.ToString(),
                CodigoImp = codigoImp.ToString(),
                Csv = lstCsv
            };
        }

        private static void TratarCodigoTituloSharedStep(WorkItem sharedStep, SharedStepDefinition sharedStepDef)
        {
            _suiteSpecs = new List<SpecDefinition>();

            var specTitulo = GerarCodigoSpecStep(
                sharedStep,
                $"SS - {DePara(sharedStep.Title)} [{sharedStep.Id}]",
                null,
                true);

            var parametrosDinamicosPassos = sharedStepDef.StepsSpec
                .Where(x => x.DynamicParameters != null)
                .SelectMany(x => x.DynamicParameters)
                .ToList();

            var parametrosSemOrigem = parametrosDinamicosPassos
                .Except(specTitulo.DynamicParameters)
                .ToList();

            // Se possui parâmetros que virão do chamador e não estão no título
            // Caso não suportado pelo Gauge, onde o conceito deveria receber uma tabela e os passos
            // do conceito acessariam as colunas da tabela.
            // Da para receber apenas uma tabela e repassar a tabela inteira para os passos
            // Nesse caso, como alternativa, será criado um step contendo todos os passos do conceito,
            // e então, recebendo uma tabela de entrada chamará os passos informando os parâmetros corretos
            if (parametrosSemOrigem.Any())
            {
                int indexIdSS = specTitulo.Code.IndexOf($"[{sharedStep.Id}]");

                if (_usarTabelaSharedStep)
                {
                    specTitulo.Code = specTitulo.Code.Insert(indexIdSS, "<tabela> ");
                    specTitulo.UsedParameters = specTitulo.UsedParameters.Concat(new string[] { "tabela" }).ToArray();
                    specTitulo.DynamicParameters = specTitulo.DynamicParameters.Concat(new string[] { "tabela" }).ToArray();
                    specTitulo.IsTableParameter = true;
                }
                else
                {
                    string parametrosTitulo = string.Join(" ", parametrosSemOrigem.Select(x => $"<{x}>").ToArray());
                    parametrosTitulo += " ";
                    specTitulo.Code = specTitulo.Code.Insert(
                        indexIdSS,
                        parametrosTitulo);
                    specTitulo.UsedParameters = specTitulo.UsedParameters.Concat(parametrosSemOrigem).ToArray();
                    specTitulo.DynamicParameters = specTitulo.UsedParameters.Concat(parametrosSemOrigem).ToArray();
                }
            }

            sharedStepDef.TitleSpecStepCode = specTitulo.Code;

            // Trocando "* " por "# "
            specTitulo.Code = "# " + specTitulo.Code.Substring(2, specTitulo.Code.Length - 2);

            sharedStepDef.TitleSpec = specTitulo;
            sharedStepDef.Title = specTitulo.Code;
        }

        private static string GerarNomeArquivoCsv(string tituloCenario)
        {
            return TratarNomeArquivo(tituloCenario + ".csv");
        }

        private static SpecDefinition GerarCodigoSpecStep(WorkItem workItem, Step step, string nomeTela, bool isSharedStep = false, bool addTableParameter = false)
        {
            return GerarCodigoSpecStep(workItem, step.ParameterizedString[0].Text, nomeTela, isSharedStep, addTableParameter);
        }

        private static SpecDefinition GerarCodigoSpecStep(WorkItem workItem, string textSpecStep, string nomeTela, bool isSharedStep = false, bool addTableParameter = false)
        {
            // Remover tags html
            string codigoSpecStep = _regexPalavraTag.Replace(textSpecStep, "");
            codigoSpecStep = codigoSpecStep.Replace("&nbsp;", " ").Replace("\r\n", " ").Replace("\n", " ");
            codigoSpecStep = new Regex(@"(?i)-\sstep\sby\sstep").Replace(codigoSpecStep, "");

            // Removendo espaços excedentes
            codigoSpecStep = AplicarRegexRecursiva(codigoSpecStep, _regexEspacosExcedentes, " ");

            codigoSpecStep = DePara(codigoSpecStep);

            codigoSpecStep = codigoSpecStep.Replace("\"", "");

            var matches = _regexParametroStepTestCase.Matches(codigoSpecStep);

            var parametrosStep = matches
                .OfType<Match>()
                .Select(x =>
                {
                    string paramTratado = TratarNomeParametro(x.Groups[1].Value);

                    int posicaoInicio = codigoSpecStep.IndexOf("@" + x.Groups[1].Value);
                    int tamanho = 1 + x.Groups[1].Value.Length;
                    codigoSpecStep = codigoSpecStep.Remove(posicaoInicio, tamanho);
                    codigoSpecStep = codigoSpecStep.Insert(posicaoInicio, $"@{paramTratado}");

                    return paramTratado;
                })
                .ToList();

            codigoSpecStep = codigoSpecStep.Trim();

            codigoSpecStep = $"* {codigoSpecStep}";

            var specDef = new SpecDefinition
            {
                Code = codigoSpecStep,
                OriginalCode = codigoSpecStep,
                OriginalText = textSpecStep,
                IsTableParameter = false,
                Comment = new List<string>(),
                DynamicParameters = new string[0],
                UsedParameters = new string[0]
            };

            PreencherValorParametros(workItem, parametrosStep, specDef, isSharedStep);

            if (addTableParameter)
            {
                specDef.Code += " <tabela>";
                specDef.UsedParameters = specDef.UsedParameters.Concat(new string[] { "tabela" }).ToArray();
                specDef.DynamicParameters = specDef.DynamicParameters.Concat(new string[] { "tabela" }).ToArray();
                specDef.IsTableParameter = true;
            }

            if (!string.IsNullOrWhiteSpace(nomeTela))
                specDef.Code += $" [{nomeTela}]";

            var specIgual = _suiteSpecs
                .FirstOrDefault(x =>
                    x.IsShared == specDef.IsShared &&
                    x.IsTableParameter == specDef.IsTableParameter &&
                    string.Equals(x.OriginalCode, specDef.OriginalCode, StringComparison.OrdinalIgnoreCase) &&
                    !x.Comment.Except(specDef.Comment).Any() &&
                    !x.DynamicParameters.Except(specDef.DynamicParameters).Any() &&
                    !x.UsedParameters.Except(specDef.UsedParameters).Any());

            // Se existe um step idêntico
            if (specIgual != null)
                return specIgual;

            var specCodigoIgual = _suiteSpecs
                .FirstOrDefault(x => x.OriginalCode == specDef.OriginalCode);

            // Se não existe step idêntico mas existe com nome igual porém com parâmetros diferentes
            if (specCodigoIgual != null)
                // Adicionando o id do testcase no nome do step
                specDef.Code += $" [{workItem.Id}]";

            _suiteSpecs.Add(specDef);

            return specDef;
        }

        private static void PreencherValorParametros(
            WorkItem workItem,
            IEnumerable<string> parametrosStep,
            SpecDefinition specDef,
            bool creatingASharedStep = false)
        {
            List<string> parametrosDinamicos = new List<string>();
            foreach (var parametro in parametrosStep)
            {
                var valorParametro = BuscarValorParametro(parametro, workItem, creatingASharedStep);

                // Parâmetro dinâmico. Ex: <param>
                if (specDef.Code.Contains($"<{ parametro }>"))
                {
                    specDef.Code = specDef.Code.Replace($"<{ parametro }>",
                        specDef.IsTableParameter ? $"<{ valorParametro }>" : $"\"{ valorParametro }\"");
                    parametrosDinamicos.Add(parametro);
                }
                // Parâmetro dinâmico e está sendo criado um shared step novo. NÃO é um cenário de test case.
                else if (creatingASharedStep && string.IsNullOrWhiteSpace(valorParametro))
                {
                    specDef.Code = specDef.Code.Replace($"@{parametro}", $"<{ parametro }>");
                    parametrosDinamicos.Add(parametro);
                }
                else
                    specDef.Code = specDef.Code.Replace($"@{parametro}", $"\"{ valorParametro }\"");
            }

            specDef.UsedParameters = parametrosStep.ToArray();
            specDef.DynamicParameters = parametrosDinamicos.ToArray();
        }

        private static void TratarNomeParametros(WorkItem workItem)
        {
            HashSet<string> hsOld;
            HashSet<string> hs = new HashSet<string>();
            if (workItem.ParametersValue != null && workItem.ParametersValue.Columns.Count > 0)
            {
                hsOld = new HashSet<string>(workItem.ParametersValue.Columns.OfType<DataColumn>().Select(x => x.ColumnName).ToArray());
                foreach (System.Data.DataColumn column in workItem.ParametersValue.Columns)
                {
                    string novo = TratarNomeParametro(column.ColumnName);
                    if (!hs.Add(novo))
                        novo += "1";

                    if (column.ColumnName != novo && hsOld.Contains(novo))
                    {
                        string tempNome = novo + "TEMP";
                        workItem.ParametersValue.Columns[novo].ColumnName = tempNome;

                        column.ColumnName = novo;

                        workItem.ParametersValue.Columns[tempNome].ColumnName = novo + "1";
                    }
                    else
                        column.ColumnName = novo;
                }
            }

            hs.Clear();

            if (workItem.SharedParametersMap != null && workItem.SharedParametersMap.parameterMap != null)
                workItem.SharedParametersMap.parameterMap
                    .Select(x =>
                    {
                        string novo = TratarNomeParametro(x.localParamName);
                        if (!hs.Add(novo))
                            novo += "1";

                        x.localParamName = novo;

                        return 1;
                    })
                    .ToArray();

            hs.Clear();

            if (workItem.ParameterSet?.ParamData?.DataRow.Count > 0)
                workItem.ParameterSet.ParamData.DataRow
                    .Where(x => x.Kvp != null)
                    .SelectMany(x => x.Kvp)
                    .Select(x =>
                    {
                        string novo = TratarNomeParametro(x.Key);
                        if (!hs.Add(novo))
                            novo += "1";

                        x.Key = novo;

                        return 1;
                    })
                    .ToArray();

            hs.Clear();

            if (workItem.ParameterSet?.ParamNames?.Param?.Count > 0)
                workItem.ParameterSet.ParamNames.Param
                    .ToArray()
                    .Select((x, i) =>
                    {
                        if (string.IsNullOrWhiteSpace(x))
                            return 1;

                        string novo = TratarNomeParametro(x);
                        if (!hs.Add(novo))
                            novo += "1";

                        workItem.ParameterSet.ParamNames.Param[i] = novo;

                        return 1;
                    })
                    .ToArray();
        }

        private static string TratarNomeParametro(string nomeParametro)
        {
            nomeParametro = removerAcentos(nomeParametro);

            // TitleCase está dando mais trabalho do que solução nesse caso. Parâmetros não possuem espaço entre as palavras
            //// Trocando caracteres especiais por espaço, para quando colocar em titleCase não deixar paravras separadas por _ em minúsculo. Ex tabela_cobranca -> tabelaCobranca
            //nomeParametro = _regexDiferenteDeLetraNumero.Replace(nomeParametro, " ");

            //if (new Regex("\\s", RegexOptions.Compiled).IsMatch(nomeParametro))
            //{
            //    TextInfo textInfo = new CultureInfo("pr-Br", false).TextInfo;
            //    nomeParametro = textInfo.ToTitleCase(nomeParametro);
            //}

            nomeParametro = _regexDiferenteDeLetraNumero.Replace(nomeParametro, "");

            // Primeira letra minúscula
            if (!string.IsNullOrWhiteSpace(nomeParametro))
                nomeParametro = nomeParametro.First().ToString().ToLower() + nomeParametro.Substring(1);

            return nomeParametro;
        }

        private static string BuscarValorParametro(
        string nomeParametro,
        WorkItem workItem,
        bool creatingASharedStep = false)
        {
            if (creatingASharedStep && nomeParametro == "tabela")
                return nomeParametro;

            if (nomeParametro == "tabela")
                return "table:resources/" + GerarNomeArquivoCsv(workItem.Title + " " + workItem.Id);

            if (workItem.ParametersValue == null &&
                workItem.SharedParametersMap == null)
                return string.Empty;

            if (workItem.ParametersValue != null)
            {
                if (workItem.ParametersValue.Rows.Count <= 0 ||
                   (!workItem.ParametersValue.Columns.Contains(nomeParametro)))
                    return string.Empty;

                if (workItem.ParametersValue.Rows.Count > 1)
                    System.Console.WriteLine($"TestCase com mais de 1 linha de parâmetros: {workItem.Id} - {workItem.Title}");

                return workItem.ParametersValue.Rows[0][nomeParametro]?.ToString();
            }
            else
            {
                var paramMap = workItem.SharedParametersMap.parameterMap
                    .FirstOrDefault(x => x.localParamName.Trim() == nomeParametro);

                if (paramMap == null)
                    return string.Empty;

                WorkItem sharedParameter;

                if (!_dicSharedParameters.TryGetValue(paramMap.sharedParameterDataSetId.ToString(), out sharedParameter))
                    return string.Empty;

                if (sharedParameter.ParameterSet.ParamData.DataRow.Count > 1)
                    System.Console.WriteLine($"SharedParameter com mais de 1 linha de parâmetros: {sharedParameter.Id} - {sharedParameter.Title} | TestCase: {workItem.Id} - {workItem.Title}");

                return sharedParameter.ParameterSet.ParamData.DataRow[0].Kvp
                    .FirstOrDefault(x => x.Key == paramMap.sharedParameterName)?.Value ?? string.Empty;
            }
        }

        private static ImpDefinition GerarCodigoImplementacao(SpecDefinition specDefinition)
        {
            // Removendo o "* " do começo
            string codigoSpecStep = specDefinition.Code.Substring(2, specDefinition.Code.Length - 2);

            // Trocando aspas duplas por tag - "abc" -> <abc>
            string nomeSpecStepTratado = _regexPalavraAspas.Replace(codigoSpecStep, replaceRegexPalavraAspasPorTag);


            List<(string tipo, string parametro)> lstParametros = new List<(string tipo, string parametro)>();
            if (specDefinition.UsedParameters?.Length > 0)
            {
                lstParametros = specDefinition.UsedParameters
                    .Select(x => (specDefinition.IsTableParameter ? "Table" : "string", x))
                    .ToList();

                // replace valor parâmetro pelo nome, exceto os parâmetros dinâmicos que já vem com o nome
                int posicao = 0;
                specDefinition.UsedParameters
                    .Where(x => !specDefinition.DynamicParameters.Contains(x))
                    .Select(x =>
                    {
                        int posicaoInicio = nomeSpecStepTratado.IndexOf('<', posicao);
                        int posicaoFim = nomeSpecStepTratado.IndexOf('>', posicao);
                        nomeSpecStepTratado = nomeSpecStepTratado.Remove(posicaoInicio, posicaoFim - posicaoInicio + 1);
                        nomeSpecStepTratado = nomeSpecStepTratado.Insert(posicaoInicio, $"<{x}>");
                        posicao = nomeSpecStepTratado.IndexOf('>', posicao) + 1;
                        return 1;
                    })
                    .ToArray();
            }

            string nomeMetodo = _regexPalavraTag.Replace(nomeSpecStepTratado, "");

            nomeMetodo = removerAcentos(nomeMetodo);

            TextInfo textInfo = new CultureInfo("pr-Br", false).TextInfo;
            nomeMetodo = textInfo.ToTitleCase(nomeMetodo.ToLower());

            nomeMetodo = _regexDiferenteDeLetraNumero.Replace(nomeMetodo, "");

            string parametros = string.Join(",", lstParametros
                .Select(x => x.tipo + " " + x.parametro)
                .ToList());

            string impMetodo = string.Empty;
            if (_regexAgrupamentoAssert.IsMatch(TratarTextoStepParaAgrupamento(specDefinition.OriginalText)))
                impMetodo = GerarImplementacaoAssertCampo(specDefinition, false);
            if (_regexAgrupamentoPreenchimento.IsMatch(TratarTextoStepParaAgrupamento(specDefinition.OriginalText)))
                impMetodo = GerarImplementacaoPreenchimentoCampo(specDefinition, false);

            var codigo =
    $@"        [Step(""{nomeSpecStepTratado}"")]
        public void {nomeMetodo}({parametros})
        {{
            {impMetodo}
        }}";

            return new ImpDefinition()
            {
                Code = codigo,
                Parameters = lstParametros,
                MethodName = nomeMetodo
            };
        }

        private static string AplicarRegexRecursiva(string word, Regex regex, string replacePattern)
        {
            bool isMatch;
            string newString;

            newString = word;
            isMatch = regex.IsMatch(word);

            while (isMatch)
            {
                newString = regex.Replace(newString, replacePattern);
                isMatch = regex.IsMatch(newString);
            }

            return newString;
        }

        private static string DePara(string palavra)
        {
            return _dePara.ContainsKey(palavra) ? _dePara[palavra] : palavra;
        }

        private static List<WorkItem> LerArquivo(string filePath)
        {
            if (!File.Exists(filePath))
                throw new InvalidOperationException($"Não foi encontrado o arquivo informado {filePath}");

            List<WorkItem> lstWorkItem = new List<WorkItem>();
            using (TextReader reader = File.OpenText(filePath))
            {
                CsvReader csv = new CsvReader(reader);
                csv.Configuration.Delimiter = ",";
                csv.Configuration.MissingFieldFound = null;

                // Validando e pulando o cabeçalho
                if (!csv.Read())
                    throw new InvalidOperationException($"Arquivo {filePath} em branco");

                while (csv.Read())
                {
                    var workItem = new WorkItem()
                    {
                        Id = csv[1],
                        Type = csv[2],
                        Title = csv[3],
                        StepsString = csv[4],
                        ParametersString = csv[5],
                        LocalDataSourceString = csv[6]
                    };
                    lstWorkItem.Add(workItem);
                }
            }

            if (lstWorkItem.Count <= 0)
                throw new InvalidOperationException($"Arquivo {filePath} sem nenhuma linha de valor");

            lstWorkItem.ForEach(x =>
            {
                if (!string.IsNullOrWhiteSpace(x.StepsString))
                {
                    if (x.StepsString[0] == '<')
                    {
                        XmlSerializer serSteps = new XmlSerializer(typeof(Steps));

                        using (TextReader reader = new StringReader(x.StepsString))
                        {
                            x.Steps = (Steps)serSteps.Deserialize(reader);
                        }
                    }
                    else // apenas 1 step, vem só o valor, fora do xml
                        x.Steps = new Steps()
                        {
                            Step = new List<Step>() { new Step() { ParameterizedString = new List<ParameterizedString>() {
                            new ParameterizedString() { Text = x.StepsString } } } }
                        };
                }

                if (!string.IsNullOrWhiteSpace(x.ParametersString))
                {
                    // Valores do shared parameter
                    if (x.ParametersString.IndexOf("<parameterSet>") != -1)
                    {
                        XmlSerializer serParameterSet = new XmlSerializer(typeof(ParameterSet));

                        using (TextReader reader = new StringReader(x.ParametersString))
                        {
                            x.ParameterSet = (ParameterSet)serParameterSet.Deserialize(reader);
                        }
                    }
                    // Estrutura dos parâmetros em outros tipos de workitems
                    else
                    {
                        XmlSerializer serParameters = new XmlSerializer(typeof(Parameters));

                        using (TextReader reader = new StringReader(x.ParametersString))
                        {
                            x.Parameters = (Parameters)serParameters.Deserialize(reader);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(x.LocalDataSourceString))
                {
                    // XML
                    if (x.LocalDataSourceString[0] == '<')
                    {
                        x.ParametersValue = new DataTable();
                        using (TextReader reader = new StringReader(x.LocalDataSourceString))
                        {
                            var ds = new DataSet();
                            ds.ReadXml(reader);
                            x.ParametersValue = ds.Tables.Count > 0 ? ds.Tables[0] : new DataTable();
                            //x.ParametersValue.ReadXml(reader);
                        }
                    }
                    // Json
                    else
                    {
                        x.SharedParametersMap = JsonConvert.DeserializeObject<LocalDataSourceJson>(x.LocalDataSourceString);
                    }
                }
            });

            return lstWorkItem;
        }

        private static string ArquivoInicialSpec(string formattedTitle)
        {
            return
                $@"{formattedTitle}
Criado pelo gerador {DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")}

";
        }

        private static string ArquivoInicialSpec(string title, string id)
        {
            return ArquivoInicialSpec($@"# {title} [{id}]");
        }

        private static string ArquivoInicialImp(string parentTitle, string title)
        {
            return
                $@"
using System;
using FluentAssertions;
using FluentAssertions.Execution;
using Gauge.CSharp.Lib;
using Gauge.CSharp.Lib.Attribute;
using {solutionNamespace}.{outputPathPage.Replace("/", ".")}.{parentTitle}.{title};

namespace {solutionNamespace}.{outputPathImp.Replace("/", ".")}.{parentTitle}.{title}
{{
    public class {title}PageStep
    {{
        private {title}Page _page = new {title}Page();
    
    }}
}}";
        }

        private static string ArquivoInicialPage(string parentTitle, string title)
        {
            return
                $@"
using OpenQA.Selenium;
using Selenium.Utils;

namespace {solutionNamespace}.{outputPathPage.Replace("/", ".")}.{parentTitle}.{title}
{{
    public class {title}Page
    {{
    
    }}
}}";
        }

        private static SharedStepDefinition BuscarSharedStepPorId(string codigo)
        {
            return _lstSharedStepDef.FirstOrDefault(x => x.Id == codigo);
        }

        private static SpecDefinition BuscarSharedStepConfig(WorkItem testCase, string titulo)
        {
            var ss = _lstSharedStep.FirstOrDefault(x => x.De == titulo);

            if (ss == null)
            {
                ss = _lstSharedStep
                    .FirstOrDefault(x =>
                        string.IsNullOrWhiteSpace(x.De) &&
                        (!string.IsNullOrWhiteSpace(x.RegexDe)) &&
                        Regex.IsMatch(titulo, x.RegexDe, RegexOptions.Compiled)
                    );
            }

            if (ss == null)
                return null;

            string codigo = ss.Para;
            if (ss.ParamDePara != null)
            {
                string paramTitulo = Regex.Match(titulo, ss.RegexParam).Groups[1].Value;
                string valorParam;
                if (string.IsNullOrWhiteSpace(paramTitulo))
                    valorParam = "TODO: CONFIGURAR";
                else
                    valorParam = ss.ParamDePara.GetValueOrDefault(paramTitulo) ?? "TODO: CONFIGURAR";

                codigo = string.Format(codigo, valorParam);
            }
            else if (ss.RegexParam != null)
            {
                titulo = _regexPalavraTag.Replace(titulo, "");
                string[] paramTitulo = Regex.Matches(titulo, ss.RegexParam)
                    .OfType<Match>()
                    .Where(x => x.Success)
                    .Select(x => x.Groups[1].Value)
                    .ToArray();

                paramTitulo = paramTitulo
                    .Select(x => string.IsNullOrWhiteSpace(x) ? "TODO: CONFIGURAR" : x)
                    .ToArray();

                if (paramTitulo.Length > 0)
                    codigo = string.Format(codigo, paramTitulo);
            }

            var spec = new SpecDefinition()
            {
                Code = codigo,
                IsShared = true
            };

            var parametros = _regexParametroStepTestCase.Matches(codigo)
                .OfType<Match>()
                .Where(x => x.Success)
                .Select(x => x.Groups[1].Value)
                .ToArray();

            if (parametros.Length > 0)
            {
                PreencherValorParametros(testCase, parametros, spec);
            }

            return spec;
        }

        private static async Task CarregarTestSuite()
        {
            if (_lstTestSuiteFolhas != null)
                return;

            var lstTestSuite = await BuscarTestSuite();

            // Todo: Filtrar suite 
            // Separando suites
            var parentIdsTestSuite = lstTestSuite.Select(x => x.parent?.id).ToList();
            var lstTestSuiteFolhas = lstTestSuite
                .Where(x =>
                    (_idsTestSuite == null || _idsTestSuite.Contains(x.id)) &&
                    !parentIdsTestSuite.Contains(x.id.ToString())) // Itens que não são pais de ninguém
                .ToList();

            var lstTestSuite2nivel = lstTestSuite
                .Where(x => lstTestSuiteFolhas.Any(y => y.parent.id == x.id.ToString()))
                .ToList();

            var lstTodasFolhas = lstTestSuiteFolhas;

            lstTestSuiteFolhas = lstTestSuiteFolhas
                .DistinctBy(x => TratarNomeArquivo(DePara(x.name), false))
                .ToList();

            if (lstTodasFolhas.Count > lstTestSuiteFolhas.Count)
                System.Console.WriteLine($"As seguintes test suites possuem " +
                    $"nomes iguais a outras já existentes, fazendo com " +
                    $"que estas sejam ignoradas: {string.Join(", ", lstTodasFolhas.Except(lstTestSuiteFolhas).Select(x => x.name).ToArray()) }");

            _lstTestSuiteFolhas = lstTestSuiteFolhas;
            _lstTestSuite2Nivel = lstTestSuite2nivel;

            await _lstTestSuiteFolhas.ParallelForEachAsync(async x =>
            {
                var ids = await BuscarTestCaseIds(x.testCasesUrl);
                x.IdsTestCasesOrdenados = ids;
                x.IdsTestCases = new HashSet<string>(ids);
            });
        }

        private static async Task<IEnumerable<string>> BuscarTestCaseIds(string url)
        {
            var result = await ConsultarAzureDevops<dynamic>(url);

            if (result == null || result.Count <= 0)
                return new List<string>();

            return result
                .Select(x => x.testCase.id.Value)
                .Cast<string>()
                .ToList();
        }

        private static async Task<IEnumerable<TestSuite>> BuscarTestSuite()
        {
            string url = $"{azureBaseUrl}_apis/test/Plans/{testPlanId}/suites?{azureApiVersion}";

            var result = await ConsultarAzureDevops<TestSuite>(url);

            if (result == null || result.Count <= 0)
                throw new InvalidOperationException($"Nenhum registro foi retornado da Api: {url}");

            return result;
        }

        private static async Task<List<T>> ConsultarAzureDevops<T>(string url)
        {
            RestClient client = new RestClient(url);
            var request = new RestRequest(url, Method.GET);
            request.AddHeader("Authorization", $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + azureToken))}");
            client.UseSerializer(() => new NewtonsoftJsonRestSerializer());

            var result = await client.GetAsync<AzureRestResult<T>>(request);

            return result?.value;
        }
    }

    public class SharedStepDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<SpecDefinition> StepsSpec { get; set; }
        public List<ImpDefinition> StepsImp { get; set; }
        public SpecDefinition TitleSpec { get; set; }
        public string TitleSpecStepCode { get; set; }
        public ImpDefinition TitleImp { get; set; }
        public bool TitleTableParameterAcessingFields { get; set; }
    }

    public class ScenarioDefinition
    {
        public string Title { get; set; }
        public List<SpecDefinition> StepsSpec { get; set; }
        public List<ImpDefinition> StepsImp { get; set; }
        public string Csv { get; set; }
    }

    public class SpecDefinition
    {
        public string Code { get; set; }
        public string OriginalText { get; set; }
        public string OriginalCode { get; set; }
        public string[] UsedParameters { get; set; }
        public string[] DynamicParameters { get; set; }
        public bool IsTableParameter { get; set; }
        public bool IsShared { get; set; }
        public List<string> Comment { get; set; }
    }

    public class ImpDefinition
    {
        public string Code { get; set; }
        public List<(string tipo, string parametro)> Parameters { get; set; }
        public string MethodName { get; set; }
    }

    public class StepGroupConfig
    {
        public Regex RegexAgrupamento { get; set; }
        public Action<WorkItem,
            List<SpecDefinition>,
            List<ImpDefinition>,
            List<Step>,
            TestSuite,
            string,
            bool> MetodoProcessarAgrupamento
        { get; set; }
    }

    public class ResultadoProcessamentoString
    {
        public string CodigoSpec { get; set; }
        public string CodigoImp { get; set; }
        public List<(string csv, string nomeArquivo)> Csv { get; set; }
    }

    public class GerarCodigoTestCaseRequest
    {
        public WorkItem TestCase { get; set; }
        public TestSuite Suite { get; set; }
        public string NomeTela { get; set; }
        public string Secao { get; set; }
        public bool IsSharedStep { get; set; }
    }
}
