using GaugeTestsGenerator.Core;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using Dasync.Collections;

namespace GaugeTestsGenerator.Azure
{
    public class AzureDataProvider : IDataProvider
    {
        private readonly AzureConfig _config;
        private readonly IWordReplacer _wordReplacer;
        private readonly ILogger _logger;
        public List<TestSuite> _lstTestSuite2Level;
        public List<TestSuite> _lstTestSuiteLeafs;
        private static Regex _regexNotLetterOrNumber = new Regex(@"[^0-9a-zA-Z]+", RegexOptions.Compiled);

        public AzureDataProvider(AzureConfig config, IWordReplacer wordReplacer, ILogger logger)
        {
            this._config = config;
            this._wordReplacer = wordReplacer;
            this._logger = logger;
        }

        public async Task LoadTests(IDataProviderFilter filter)
        {
            if (filter is null)
                throw new ArgumentNullException(nameof(filter));

            var azureFilter = filter as AzureDataProviderFilter;
            if (azureFilter is null)
                throw new InvalidOperationException();

            await LoadTestSuites(azureFilter);
        }

        private async Task LoadTestSuites(AzureDataProviderFilter filter)
        {
            if (_lstTestSuiteLeafs != null)
                return;

            var lstTestSuite = await GetTestSuite(filter);

            // Todo: Filtrar suite 
            // Separando suites
            var parentIdsTestSuite = lstTestSuite.Select(x => x.parent?.id).ToList();
            var lstTestSuiteFolhas = lstTestSuite
                .Where(x =>
                    (filter.TestSuiteIds == null || filter.TestSuiteIds.Contains(x.id)) &&
                    !parentIdsTestSuite.Contains(x.id.ToString())) // Itens without parents
                .ToList();

            var lstTestSuite2nivel = lstTestSuite
                .Where(x => lstTestSuiteFolhas.Any(y => y.parent.id == x.id.ToString()))
                .ToList();

            var lstTodasFolhas = lstTestSuiteFolhas;

            lstTestSuiteFolhas = IgnoreSimilarNames(lstTestSuiteFolhas, x => x.name)
                .ToList();

            if (lstTodasFolhas.Count > lstTestSuiteFolhas.Count)
                _logger.LogInformation($"As seguintes test suites possuem " +
                    $"nomes iguais a outras já existentes, fazendo com " +
                    $"que estas sejam ignoradas: {string.Join(", ", lstTodasFolhas.Except(lstTestSuiteFolhas).Select(x => x.name).ToArray()) }");

            _lstTestSuiteLeafs = lstTestSuiteFolhas;
            _lstTestSuite2Level = lstTestSuite2nivel;

            await _lstTestSuiteLeafs.ParallelForEachAsync(async x =>
            {
                var ids = await GetTestCaseIds(x.testCasesUrl);
                x.IdsTestCasesOrdenados = ids;
                x.IdsTestCases = new HashSet<string>(ids);
            });
        }

        private async Task<IEnumerable<string>> GetTestCaseIds(string url)
        {
            var result = await GetFromAzureDevops<dynamic>(url);

            if (result == null || result.Count <= 0)
                return new List<string>();

            return result
                .Select(x => x.testCase.id.Value)
                .Cast<string>()
                .ToList();
        }

        private IEnumerable<T> IgnoreSimilarNames<T>(IEnumerable<T> source, Func<T, string> keySelector)
        {
            return source
                .DistinctBy(x => CleaningSpacesAndSpecialCharacteres(_wordReplacer.FromTo(keySelector(x))));
        }

        private string CleaningSpacesAndSpecialCharacteres(string word)
        {
            word = RemoveAccents(word);

            TextInfo textInfo = CultureInfo.DefaultThreadCurrentCulture.TextInfo;
            word = textInfo.ToTitleCase(word.ToLower());

            word = _regexNotLetterOrNumber.Replace(word, "");

            return word;
        }

        private static string RemoveAccents(string texto)
        {
            string comAcentos = "ÄÅÁÂÀÃäáâàãÉÊËÈéêëèÍÎÏÌíîïìÖÓÔÒÕöóôòõÜÚÛüúûùÇç";
            string semAcentos = "AAAAAAaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUuuuuCc";

            for (int i = 0; i < comAcentos.Length; i++)
            {
                texto = texto.Replace(comAcentos[i].ToString(), semAcentos[i].ToString());
            }
            return texto;
        }

        private async Task<IEnumerable<TestSuite>> GetTestSuite(AzureDataProviderFilter filter)
        {

            string url = $"{_config.ServerUrl}_apis/test/Plans/{filter.TestPlanId}/suites?{_config.ApiVersion}";

            var result = await GetFromAzureDevops<TestSuite>(url);

            if (result == null || result.Count <= 0)
                throw new InvalidOperationException($"Nenhum registro foi retornado da Api: {url}");

            return result;
        }

        private async Task<List<T>> GetFromAzureDevops<T>(string url)
        {
            RestClient client = new RestClient(url);
            var request = new RestRequest(url, Method.GET);
            request.AddHeader("Authorization", $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + _config.Token))}");
            client.UseSerializer(() => new NewtonsoftJsonRestSerializer());

            var result = await client.GetAsync<AzureRestResult<T>>(request);

            return result?.value;
        }
    }
}
